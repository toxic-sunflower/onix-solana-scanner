using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Onix.Scanner.Core;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Api.Services;

/// <summary>
/// One async worker task per enabled token. A token's failure/timeout never blocks
/// other tokens; concurrency and pacing per proxy (or the shared/no-proxy group) is
/// capped so free-tier rate limits aren't hammered.
/// </summary>
public sealed class JupiterWorkerService : BackgroundService
{
    private readonly ITokenSnapshotPool _snapshotPool;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JupiterWorkerService> _logger;

    // NOTE: verify against https://developers.jup.ag/ before release (per TZ Appendix A) —
    // the lite-api endpoint below is the current free-tier Quote API.
    private const string QuoteApiBase = "https://lite-api.jup.ag/swap/v1/quote";
    private const int PollIntervalMs = 1000;
    private const int RequestTimeoutSeconds = 4;
    private const int GroupConcurrency = 5;
    private const int MinIntervalPerGroupMs = 250;
    private static readonly TimeSpan ProxyErrorTtl = TimeSpan.FromSeconds(30);

    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds),
        DefaultRequestHeaders = { { "User-Agent", "OnixScanner/1.0" } }
    };

    private readonly ConcurrentDictionary<string, GroupLimiter> _groupLimiters = new();

    private sealed class GroupLimiter
    {
        public SemaphoreSlim Concurrency { get; } = new(GroupConcurrency, GroupConcurrency);
        public SemaphoreSlim Pacing { get; } = new(1, 1);
        public DateTime NextAllowedStart { get; set; } = DateTime.MinValue;
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
            var quoteAmounts = await tokenRepo.GetAllQuoteAmountsAsync(stoppingToken);
            var proxyMap = proxies.Where(p => p.Enabled).ToDictionary(p => p.Id);

            var enabled = tokens
                .Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.SolanaMint) && !string.IsNullOrWhiteSpace(t.JupiterInputMint))
                .ToList();

            if (enabled.Count > 0)
            {
                var tasks = enabled.Select(token =>
                {
                    var proxy = token.ProxyId.HasValue && proxyMap.TryGetValue(token.ProxyId.Value, out var p) ? p : null;
                    var quoteAmount = quoteAmounts.GetValueOrDefault(token.Id, 0.01m);
                    return FetchTokenQuoteAsync(token, proxy, quoteAmount, stoppingToken);
                });
                await Task.WhenAll(tasks);
            }

            await Task.Delay(PollIntervalMs, stoppingToken);
        }
    }

    private async Task FetchTokenQuoteAsync(Token token, Proxy? proxy, decimal quoteAmount, CancellationToken ct)
    {
        var groupKey = proxy?.Id.ToString() ?? "__shared";
        var limiter = _groupLimiters.GetOrAdd(groupKey, _ => new GroupLimiter());

        await limiter.Concurrency.WaitAsync(ct);
        try
        {
            await limiter.Pacing.WaitAsync(ct);
            try
            {
                var now = DateTime.UtcNow;
                if (now < limiter.NextAllowedStart)
                    await Task.Delay(limiter.NextAllowedStart - now, ct);
                limiter.NextAllowedStart = DateTime.UtcNow.AddMilliseconds(MinIntervalPerGroupMs);
            }
            finally
            {
                limiter.Pacing.Release();
            }

            var amountRaw = (long)Math.Round(quoteAmount * (decimal)Math.Pow(10, token.JupiterInputDecimals));
            if (amountRaw <= 0)
            {
                _logger.LogTrace("Jupiter: skipping {Symbol}, non-positive amount", token.Symbol);
                return;
            }

            var url = $"{QuoteApiBase}?inputMint={token.JupiterInputMint}&outputMint={token.SolanaMint}&amount={amountRaw}&slippageBps=50";
            var httpClient = proxy is not null ? CreateProxyClient(proxy) : SharedHttp;
            try
            {
                var sw = Stopwatch.StartNew();
                using var response = await httpClient.GetAsync(url, ct);

                if ((int)response.StatusCode == 429)
                {
                    limiter.NextAllowedStart = DateTime.UtcNow.AddSeconds(Random.Shared.Next(15, 31));
                    _logger.LogWarning("Jupiter rate limited for {Symbol} (group {Group})", token.Symbol, groupKey);
                    return;
                }

                response.EnsureSuccessStatusCode();
                var latencyMs = (int)sw.ElapsedMilliseconds;

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("inAmount", out var inEl) || !root.TryGetProperty("outAmount", out var outEl))
                {
                    _logger.LogTrace("Jupiter: no route for {Symbol}", token.Symbol);
                    return;
                }

                if (!long.TryParse(inEl.GetString(), out var inAtomic) || !long.TryParse(outEl.GetString(), out var outAtomic))
                    return;
                if (inAtomic <= 0 || outAtomic <= 0) return;

                var inAmount = inAtomic / (decimal)Math.Pow(10, token.JupiterInputDecimals);
                var outAmount = outAtomic / (decimal)Math.Pow(10, token.Decimals);
                var buyPrice = inAmount / outAmount;
                if (buyPrice <= 0) return;

                var scaled = buyPrice * 1e18m;
                if (scaled > long.MaxValue || scaled < long.MinValue) return;

                var idx = _snapshotPool.GetOrAddIndex(token.Id);
                ref var snap = ref _snapshotPool.GetSnapshot(idx);
                snap.JupiterBuyPriceRaw = (long)scaled;
                snap.JupiterTimestampUtc = DateTime.UtcNow.Ticks;
                snap.JupiterLatencyMs = latencyMs;
                snap.ProxyId = proxy?.Id;
                snap.ProxyErrorUntilUtc = 0;
                Interlocked.Increment(ref snap.Sequence);
            }
            finally
            {
                if (proxy is not null && !ReferenceEquals(httpClient, SharedHttp))
                    httpClient.Dispose();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            if (proxy is not null)
            {
                var idx = _snapshotPool.GetOrAddIndex(token.Id);
                ref var snap = ref _snapshotPool.GetSnapshot(idx);
                snap.ProxyErrorUntilUtc = DateTime.UtcNow.Add(ProxyErrorTtl).Ticks;
            }
            _logger.LogDebug(ex, "Jupiter quote failed for {Symbol}", token.Symbol);
        }
        finally
        {
            limiter.Concurrency.Release();
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

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds) };
    }
}
