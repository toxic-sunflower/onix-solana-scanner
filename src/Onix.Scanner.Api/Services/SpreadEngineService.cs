using System.Threading;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Onix.Scanner.Api.Hubs;
using Onix.Scanner.Core;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared;
using Onix.Scanner.Shared.Dtos;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Api.Services;

public sealed class SpreadEngineService : BackgroundService
{
    private readonly ITokenSnapshotPool _snapshotPool;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<SpreadHub> _hub;
    private readonly TelegramNotificationService? _telegram;
    private readonly ILogger<SpreadEngineService> _logger;

    private const int BatchIntervalMs = 100;
    private static int SignalVersion = 1;
    private static long _eventCounter;

    private readonly Channel<SpreadTick> _tickChannel =
        System.Threading.Channels.Channel.CreateBounded<SpreadTick>(10000);

    public SpreadEngineService(
        ITokenSnapshotPool snapshotPool,
        IServiceScopeFactory scopeFactory,
        IHubContext<SpreadHub> hub,
        ILogger<SpreadEngineService> logger,
        TelegramNotificationService? telegram = null)
    {
        _snapshotPool = snapshotPool;
        _scopeFactory = scopeFactory;
        _hub = hub;
        _telegram = telegram;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batchWriter = BatchWriteTicksAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var tokenRepo = scope.ServiceProvider.GetRequiredService<ITokenRepository>();
            var tokens = await tokenRepo.GetAllAsync(stoppingToken);

            foreach (var token in tokens.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.BingxSymbol)))
            {
                if (!_snapshotPool.TryGetIndex(token.Id, out var idx))
                    continue;

                var snap = _snapshotPool.GetSnapshot(idx);

                var quality = SpreadCalculator.CalculateQuality(
                    snap.BingxTimestampUtc, snap.JupiterTimestampUtc);
                var spread = SpreadCalculator.CalculateSpread(
                    snap.BingxAskPriceRaw, snap.JupiterBuyPriceRaw);

                var tick = new SpreadTick
                {
                    TokenId = token.Id,
                    BingxAskPrice = snap.BingxAskPriceRaw / 1e18m,
                    JupiterBuyPrice = snap.JupiterBuyPriceRaw / 1e18m,
                    SpreadPct = spread,
                    BingxReceivedAt = new DateTime(snap.BingxTimestampUtc, DateTimeKind.Utc),
                    JupiterReceivedAt = new DateTime(snap.JupiterTimestampUtc, DateTimeKind.Utc),
                    CalculatedAt = DateTime.UtcNow,
                    BingxLatencyMs = snap.BingxLatencyMs,
                    JupiterLatencyMs = snap.JupiterLatencyMs,
                    ProxyId = snap.ProxyId,
                    QualityStatus = quality,
                };

                await _tickChannel.Writer.WriteAsync(tick, stoppingToken);

                var status = SpreadCalculator.ComputeStatus(token, snap);
                var dto = new TokenCardDto
                {
                    Id = token.Id,
                    Symbol = token.Symbol,
                    BingxAskPrice = tick.BingxAskPrice,
                    JupiterBuyPrice = tick.JupiterBuyPrice,
                    SpreadPct = spread,
                    Status = status,
                    LastUpdated = DateTime.UtcNow,
                    BingxUrl = token.BingxUrl,
                    JupiterUrl = token.JupiterUrl,
                    SolscanUrl = token.SolscanUrl
                };

                var eventId = Interlocked.Increment(ref _eventCounter);
                var quotePayload = new
                {
                    version = SignalVersion,
                    event_id = eventId,
                    token_id = token.Id,
                    symbol = token.Symbol,
                    bingx_ask_price = tick.BingxAskPrice,
                    jupiter_buy_price = tick.JupiterBuyPrice,
                    spread_pct = spread,
                    bingx_received_at = tick.BingxReceivedAt,
                    jupiter_received_at = tick.JupiterReceivedAt,
                    calculated_at = tick.CalculatedAt,
                    status = status.ToString()
                };
                await _hub.Clients.Group("dashboard").SendAsync("token.quote", quotePayload, stoppingToken);

                if (status != token.Status)
                {
                    Interlocked.Increment(ref _eventCounter);
                    await _hub.Clients.Group("dashboard").SendAsync("token.status", new
                    {
                        version = SignalVersion,
                        event_id = eventId + 1,
                        token_id = token.Id,
                        status = status.ToString(),
                        bingx_status = snap.BingxTimestampUtc > 0 ? "ok" : "stale",
                        jupiter_status = snap.JupiterTimestampUtc > 0 ? "ok" : "stale"
                    }, stoppingToken);
                }

                if (spread >= SpreadCalculator.DefaultAlertThresholdPct)
                {
                    Interlocked.Increment(ref _eventCounter);
                    await _hub.Clients.Group("dashboard").SendAsync("token.alert", new
                    {
                        version = SignalVersion,
                        event_id = eventId + 2,
                        token_id = token.Id,
                        spread_pct = spread,
                        threshold = SpreadCalculator.DefaultAlertThresholdPct,
                        sent_at = DateTime.UtcNow
                    }, stoppingToken);
                }

                _telegram?.EnqueueAlert(dto);
            }

            await Task.Delay(50, stoppingToken);
        }

        _tickChannel.Writer.TryComplete();
        await batchWriter;
    }

    private async Task BatchWriteTicksAsync(CancellationToken ct)
    {
        var batch = new List<SpreadTick>(100);

        while (await _tickChannel.Reader.WaitToReadAsync(ct))
        {
            while (_tickChannel.Reader.TryRead(out var tick))
            {
                batch.Add(tick);
                if (batch.Count >= 100) break;
            }

            if (batch.Count > 0)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var spreadRepo = scope.ServiceProvider.GetRequiredService<ISpreadTickRepository>();
                    await spreadRepo.WriteBatchAsync(batch, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to write batch of {Count} ticks", batch.Count);
                }
                batch.Clear();
            }
        }
    }
}
