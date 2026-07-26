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
    private readonly ConcurrentDictionary<Guid, DateTime> _lastErrorLogAt = new();
    private static readonly TimeSpan ErrorLogThrottle = TimeSpan.FromSeconds(60);

    // Observability: per-token last successful fetch (so "is Jimothy actually
    // being polled, and how stale is it?" is a log search away instead of a
    // guess), plus per-cycle counters and a periodic summary. None of this
    // existed before — every staleness question had to be answered by
    // reasoning about the code instead of looking at data.
    private readonly ConcurrentDictionary<Guid, (string Symbol, DateTime LastSuccessAt, int LatencyMs)> _tokenHealth = new();
    private int _cycleSucceeded, _cycleRateLimited, _cycleErrored, _cycleSkippedBackoff;
    private DateTime _lastSummaryLogAt = DateTime.MinValue;
    private static readonly TimeSpan SummaryLogInterval = TimeSpan.FromSeconds(60);

    // Per-token 429 backoff. Deliberately NOT on the shared GroupLimiter: that
    // gate is keyed by proxy group, so a rate-limit hit on ANY one token in a
    // shared (no-proxy) group used to freeze every other token sharing that
    // group for 15-31s too — with ~100+ tokens piled into "__shared", this
    // was the actual cause of ticks arriving rarely for those tokens, not
    // just an unlucky one. Scoping the backoff to the offending token only
    // means the rest of the group keeps polling normally.
    private readonly ConcurrentDictionary<Guid, DateTime> _tokenBackoffUntil = new();

    /// <summary>Logs failures at Warning (visible at default log level) but at
    /// most once per token per minute — this runs per-token every ~1s, so
    /// logging every occurrence would flood the log instead of explaining
    /// anything.</summary>
    private void LogFailureThrottled(Token token, string message, Exception? ex = null)
    {
        Interlocked.Increment(ref _cycleErrored);
        var now = DateTime.UtcNow;
        var last = _lastErrorLogAt.GetOrAdd(token.Id, DateTime.MinValue);
        if (now - last < ErrorLogThrottle) return;
        _lastErrorLogAt[token.Id] = now;
        _logger.LogWarning(ex, "Jupiter quote issue for {Symbol}: {Message}", token.Symbol, message);
    }

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

            Interlocked.Exchange(ref _cycleSucceeded, 0);
            Interlocked.Exchange(ref _cycleRateLimited, 0);
            Interlocked.Exchange(ref _cycleErrored, 0);
            Interlocked.Exchange(ref _cycleSkippedBackoff, 0);

            var sweepTimer = Stopwatch.StartNew();
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
            sweepTimer.Stop();

            LogCycleSummaryThrottled(enabled, sweepTimer.Elapsed);

            await Task.Delay(PollIntervalMs, stoppingToken);
        }
    }

    /// <summary>Once a minute: how many tokens are actually enabled, how long
    /// a full sweep took, what happened to them, and which specific tokens
    /// are furthest behind on a fresh price — searchable by symbol instead
    /// of having to reason about the code to guess why one token looks
    /// stale.</summary>
    private void LogCycleSummaryThrottled(List<Token> enabled, TimeSpan sweepDuration)
    {
        var now = DateTime.UtcNow;
        if (now - _lastSummaryLogAt < SummaryLogInterval) return;
        _lastSummaryLogAt = now;

        var stale = enabled
            .Select(t => new
            {
                t.Symbol,
                Age = _tokenHealth.TryGetValue(t.Id, out var h) ? now - h.LastSuccessAt : TimeSpan.MaxValue,
            })
            .OrderByDescending(x => x.Age)
            .Take(5)
            .Select(x => x.Age == TimeSpan.MaxValue ? $"{x.Symbol}=never" : $"{x.Symbol}={x.Age.TotalSeconds:F0}s")
            .ToList();

        _logger.LogInformation(
            "Jupiter sweep: {Enabled} enabled tokens, took {SweepMs}ms, {Succeeded} ok / {RateLimited} rate-limited / {Errored} errored / {SkippedBackoff} skipped (own backoff). Stalest: {Stalest}",
            enabled.Count, (int)sweepDuration.TotalMilliseconds, _cycleSucceeded, _cycleRateLimited, _cycleErrored, _cycleSkippedBackoff,
            string.Join(", ", stale));
    }

    private async Task FetchTokenQuoteAsync(Token token, Proxy? proxy, decimal quoteAmount, CancellationToken ct)
    {
        if (_tokenBackoffUntil.TryGetValue(token.Id, out var backoffUntil) && DateTime.UtcNow < backoffUntil)
        {
            Interlocked.Increment(ref _cycleSkippedBackoff);
            return;
        }

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
                LogFailureThrottled(token, $"non-positive amount computed (quoteAmount={quoteAmount}, decimals={token.JupiterInputDecimals})");
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
                    Interlocked.Increment(ref _cycleRateLimited);
                    _tokenBackoffUntil[token.Id] = DateTime.UtcNow.AddSeconds(Random.Shared.Next(15, 31));
                    _logger.LogWarning("Jupiter rate limited for {Symbol} (group {Group}) — backing off this token only", token.Symbol, groupKey);
                    return;
                }

                var latencyMs = (int)sw.ElapsedMilliseconds;
                var json = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    LogFailureThrottled(token, $"HTTP {(int)response.StatusCode} from {url} — body: {Truncate(json)}");
                    return;
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("inAmount", out var inEl) || !root.TryGetProperty("outAmount", out var outEl))
                {
                    LogFailureThrottled(token, $"response missing inAmount/outAmount — body: {Truncate(json)}");
                    return;
                }

                if (!long.TryParse(inEl.GetString(), out var inAtomic) || !long.TryParse(outEl.GetString(), out var outAtomic))
                {
                    LogFailureThrottled(token, $"inAmount/outAmount not parseable as long — body: {Truncate(json)}");
                    return;
                }
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
                Interlocked.Increment(ref _cycleSucceeded);
                _tokenHealth[token.Id] = (token.Symbol, DateTime.UtcNow, latencyMs);
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
            LogFailureThrottled(token, $"{ex.GetType().Name}: {ex.Message}", ex);
        }
        finally
        {
            limiter.Concurrency.Release();
        }
    }

    private static string Truncate(string s) => s.Length > 300 ? s[..300] + "…" : s;

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
