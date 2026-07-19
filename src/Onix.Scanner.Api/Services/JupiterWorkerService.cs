using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Onix.Scanner.Core;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Api.Services;

public sealed class JupiterWorkerService : BackgroundService
{
    private readonly ITokenSnapshotPool _snapshotPool;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JupiterWorkerService> _logger;

    private const string PriceApiBase = "https://api.jup.ag/price/v3";
    private const int ChunkSize = 100;
    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(2),
        DefaultRequestHeaders = { { "User-Agent", "OnixScanner/1.0" } }
    };

    private readonly ConcurrentDictionary<string, GroupRateLimiter> _groupLimiters = new();

    private sealed class GroupRateLimiter
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public DateTime NextAllowed { get; set; } = DateTime.MinValue;
    }

    public JupiterWorkerService(
        ITokenSnapshotPool snapshotPool,
        IServiceScopeFactory scopeFactory,
        ILogger<JupiterWorkerService> logger)
    {
        _snapshotPool = snapshotPool;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var tokenRepo = scope.ServiceProvider.GetRequiredService<ITokenRepository>();
            var proxyRepo = scope.ServiceProvider.GetRequiredService<IProxyRepository>();
            var tokens = await tokenRepo.GetAllAsync(stoppingToken);
            var proxies = await proxyRepo.GetAllAsync(stoppingToken);
            var proxyMap = proxies.Where(p => p.Enabled).ToDictionary(p => p.Id);

            var enabled = tokens
                .Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.SolanaMint))
                .ToList();

            if (enabled.Count > 0)
            {
                var groups = enabled.GroupBy(t => t.ProxyId).ToList();
                var tasks = groups.Select(group => ProcessGroupAsync(group, proxyMap, stoppingToken));
                await Task.WhenAll(tasks);
            }
            else
            {
                await Task.Delay(2000, stoppingToken);
            }
        }
    }

    private async Task ProcessGroupAsync(IGrouping<Guid?, Token> group, Dictionary<Guid, Proxy> proxyMap, CancellationToken ct)
    {
        Proxy? proxy = group.Key.HasValue && proxyMap.TryGetValue(group.Key.Value, out var p) ? p : null;
        var tokenList = group.ToList();

        for (int i = 0; i < tokenList.Count; i += ChunkSize)
        {
            var chunk = tokenList.GetRange(i, Math.Min(ChunkSize, tokenList.Count - i));
            await BatchFetchAsync(chunk, proxy, ct);
        }
    }

    private async Task BatchFetchAsync(List<Token> tokens, Proxy? proxy, CancellationToken ct)
    {
        var key = proxy?.Id.ToString() ?? "__default";
        var limiter = _groupLimiters.GetOrAdd(key, _ => new GroupRateLimiter());

        await limiter.Semaphore.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            if (now < limiter.NextAllowed)
                await Task.Delay(limiter.NextAllowed - now, ct);
            limiter.NextAllowed = now.AddSeconds(2);

            var httpClient = proxy is not null ? CreateProxyClient(proxy) : SharedHttp;
            var ids = string.Join(",", tokens.Select(t => t.SolanaMint));
            var url = $"{PriceApiBase}?ids={Uri.EscapeDataString(ids)}";

            var sw = ValueStopwatch.StartNew();
            using var httpResponse = await httpClient.GetAsync(url, ct);

            if ((int)httpResponse.StatusCode == 429)
            {
                var backoff = Random.Shared.Next(30, 61);
                limiter.NextAllowed = DateTime.UtcNow.AddSeconds(backoff);
                _logger.LogWarning("Jupiter rate limited ({Key}), backing off {Backoff}s", key, backoff);
                return;
            }

            httpResponse.EnsureSuccessStatusCode();
            var latencyMs = (int)sw.ElapsedMilliseconds;

            var json = await httpResponse.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            foreach (var token in tokens)
            {
                if (!root.TryGetProperty(token.SolanaMint, out var entry) ||
                    !entry.TryGetProperty("usdPrice", out var priceEl))
                {
                    _logger.LogTrace("Jupiter: no price for {Symbol}", token.Symbol);
                    continue;
                }

                var price = priceEl.GetDecimal();
                if (price <= 0) continue;

                var scaled = price * 1e18m;
                if (scaled > long.MaxValue || scaled < long.MinValue)
                {
                    _logger.LogTrace("Jupiter price overflow for {Symbol}: {Price}", token.Symbol, price);
                    continue;
                }

                var idx = _snapshotPool.GetOrAddIndex(token.Id);
                ref var snap = ref _snapshotPool.GetSnapshot(idx);
                snap.JupiterBuyPriceRaw = (long)scaled;
                snap.JupiterTimestampUtc = DateTime.UtcNow.Ticks;
                snap.JupiterLatencyMs = latencyMs;
                snap.ProxyId = proxy?.Id;
                Interlocked.Increment(ref snap.Sequence);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Jupiter batch fetch failed");
        }
        finally
        {
            limiter.Semaphore.Release();
        }
    }

    private static HttpClient CreateProxyClient(Proxy proxy)
    {
        var handler = new HttpClientHandler();

        if (proxy.Type.Equals("SOCKS5", StringComparison.OrdinalIgnoreCase))
        {
            handler.Proxy = new WebProxy($"socks5://{proxy.Host}:{proxy.Port}");
        }
        else
        {
            var uri = string.IsNullOrEmpty(proxy.Username)
                ? new Uri($"http://{proxy.Host}:{proxy.Port}")
                : new Uri($"http://{proxy.Username}:{proxy.Password}@{proxy.Host}:{proxy.Port}");
            handler.Proxy = new WebProxy(uri);
        }

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
    }
}

internal readonly struct ValueStopwatch
{
    private readonly long _start;
    private ValueStopwatch(long start) => _start = start;
    public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());
    public long ElapsedMilliseconds => (Stopwatch.GetTimestamp() - _start) * 1000 / Stopwatch.Frequency;
}
