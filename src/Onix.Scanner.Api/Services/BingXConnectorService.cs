using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Onix.Scanner.Core;
using Onix.Scanner.Core.Contracts;

namespace Onix.Scanner.Api.Services;

public sealed class BingXConnectorService : BackgroundService
{
    private readonly ITokenSnapshotPool _snapshotPool;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BingXConnectorService> _logger;

    private const string WsUrl = "wss://open-api-swap.bingx.com/swap-market";

    private readonly Dictionary<string, (int Index, decimal Multiplier)> _symbolMap = new(StringComparer.OrdinalIgnoreCase);

    public BingXConnectorService(
        ITokenSnapshotPool snapshotPool,
        IServiceScopeFactory scopeFactory,
        ILogger<BingXConnectorService> logger)
    {
        _snapshotPool = snapshotPool;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = TimeSpan.FromSeconds(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
                backoff = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BingX connector crashed. Reconnecting in {Delay}s", backoff.TotalSeconds);
                await Task.Delay(backoff, stoppingToken);
                backoff = backoff.TotalSeconds < 60
                    ? backoff * 2 + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000))
                    : TimeSpan.FromSeconds(60);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var tokenRepo = scope.ServiceProvider.GetRequiredService<ITokenRepository>();
        var tokens = await tokenRepo.GetAllAsync(ct);

        _symbolMap.Clear();
        foreach (var t in tokens.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.BingxSymbol)))
        {
            var idx = _snapshotPool.GetOrAddIndex(t.Id);
            var multiplier = ParseMultiplier(t.BingxSymbol);
            _symbolMap[t.BingxSymbol] = (idx, multiplier);
        }

        if (_symbolMap.Count == 0)
        {
            _logger.LogWarning("No enabled tokens configured. Waiting...");
            await Task.Delay(10000, ct);
            return;
        }

        using var ws = new ClientWebSocket();
        _logger.LogInformation("Connecting to BingX at {Url}", WsUrl);
        await ws.ConnectAsync(new Uri(WsUrl), ct);
        _logger.LogInformation("Connected to BingX");

        foreach (var symbol in _symbolMap.Keys)
        {
            var subMsg = JsonSerializer.Serialize(new
            {
                id = Guid.NewGuid().ToString(),
                reqType = "sub",
                dataType = $"{symbol}@depth10@100ms"
            });
            await ws.SendAsync(Encoding.UTF8.GetBytes(subMsg), WebSocketMessageType.Text, true, ct);
            _logger.LogDebug("Subscribed to {Symbol} depth", symbol);
        }

        var buffer = new byte[65536];
        var messageBuffer = new MemoryStream();

        while (!ct.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(buffer, ct);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                _logger.LogWarning("BingX WebSocket closed: {Status}", result.CloseStatus);
                break;
            }

            messageBuffer.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                var rawBytes = messageBuffer.ToArray();
                messageBuffer.SetLength(0);

                if (rawBytes.Length > 0)
                {
                    var text = result.MessageType == WebSocketMessageType.Text
                        ? Encoding.UTF8.GetString(rawBytes)
                        : DecompressIfGzip(rawBytes);

                    if (text is null) continue;

                    if (text.Trim().Equals("Ping", StringComparison.OrdinalIgnoreCase))
                    {
                        var pong = Encoding.UTF8.GetBytes("Pong");
                        await ws.SendAsync(pong, WebSocketMessageType.Text, true, ct);
                        _logger.LogTrace("Ping \u2192 Pong");
                        continue;
                    }
                    ProcessMessage(text);
                }
            }
        }
    }

    private void ProcessMessage(string json)
    {
        // Handle plain text "Ping" (BingX V2 WS sends this every 5s)
        if (json.Trim().Equals("Ping", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogTrace("Ping \u2192 Pong");
            return;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Skipping non-JSON message: {Preview}", json[..Math.Min(json.Length, 80)]);
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.TryGetProperty("ping", out var pingTs))
            {
                var pong = JsonSerializer.Serialize(new { pong = pingTs.GetInt64() });
                _logger.LogTrace("JSON Ping \u2192 Pong");
                return;
            }

            if (!root.TryGetProperty("dataType", out var dataTypeEl))
                return;

            var dataType = dataTypeEl.GetString();
            if (dataType is null || !dataType.Contains("@depth"))
                return;

            var symbol = dataType.Split('@')[0];
            if (string.IsNullOrEmpty(symbol) || !_symbolMap.TryGetValue(symbol, out var entry))
                return;

            if (!root.TryGetProperty("data", out var data))
                return;

            if (!data.TryGetProperty("asks", out var asks) || asks.GetArrayLength() == 0)
                return;

            var firstAsk = asks[0];
            if (firstAsk.ValueKind != JsonValueKind.Array || firstAsk.GetArrayLength() < 2)
                return;

            var askPriceStr = firstAsk[0].GetString();
            if (askPriceStr is null) return;

            if (!decimal.TryParse(askPriceStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var askPrice))
                return;

            var pricePerToken = askPrice / entry.Multiplier;
            var scaled = pricePerToken * 1e18m;
            if (scaled > long.MaxValue || scaled < long.MinValue)
            {
                _logger.LogTrace("Price overflow for {Symbol}: {Price}", symbol, pricePerToken);
                return;
            }

            ref var snap = ref _snapshotPool.GetSnapshot(entry.Index);
            snap.BingxAskPriceRaw = (long)scaled;
            snap.BingxTimestampUtc = DateTime.UtcNow.Ticks;

            if (data.TryGetProperty("updateTime", out var updateTime) && updateTime.ValueKind == JsonValueKind.Number)
            {
                var exchangeMs = updateTime.GetInt64();
                snap.BingxExchangeTimestampUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks + exchangeMs * TimeSpan.TicksPerMillisecond;
                snap.BingxLatencyMs = (int)((DateTime.UtcNow.Ticks - snap.BingxExchangeTimestampUtc) / TimeSpan.TicksPerMillisecond);
            }
            else if (root.TryGetProperty("dataTime", out var dataTime) && dataTime.ValueKind == JsonValueKind.String &&
                     DateTime.TryParse(dataTime.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                         System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
            {
                snap.BingxExchangeTimestampUtc = dt.Ticks;
                snap.BingxLatencyMs = (int)((DateTime.UtcNow - dt).TotalMilliseconds);
            }

            Interlocked.Increment(ref snap.Sequence);

            _logger.LogTrace("BingX depth: {Symbol} ask={AskPrice}", symbol, askPrice);
        }
    }

    private static decimal ParseMultiplier(string bingxSymbol)
    {
        var i = 0;
        while (i < bingxSymbol.Length && bingxSymbol[i] >= '0' && bingxSymbol[i] <= '9')
            i++;
        if (i == 0) return 1m;
        var prefix = bingxSymbol[..i];
        if (decimal.TryParse(prefix, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var m) && m > 0)
            return m;
        return 1m;
    }

    private static string? DecompressIfGzip(byte[] data)
    {
        if (data.Length < 2 || data[0] != 0x1F || data[1] != 0x8B)
            return Encoding.UTF8.GetString(data);

        using var compressed = new MemoryStream(data);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var result = new MemoryStream();
        gzip.CopyTo(result);
        return Encoding.UTF8.GetString(result.ToArray());
    }
}
