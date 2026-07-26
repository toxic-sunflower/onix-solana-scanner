using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Onix.Scanner.Core;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Api.Services;

/// <summary>
/// One persistent async loop per enabled token (per TZ 7.1/7.3: "один токен =
/// один независимый worker"), started/stopped by a lightweight supervisor
/// that refreshes the enabled-token list once a second. A token's own loop
/// never waits on any other token's — a slow/stuck token can't delay
/// anyone else's cadence the way a shared Task.WhenAll batch would.
/// Concurrency and pacing per proxy (or the shared/no-proxy group) is still
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
    private const int SupervisorRefreshMs = 1000;
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
    // guess), plus running counters (reset each time the summary is logged)
    // and a periodic summary. None of this existed before — every staleness
    // question had to be answered by reasoning about the code instead of
    // looking at data.
    private readonly ConcurrentDictionary<Guid, (string Symbol, DateTime LastSuccessAt, int LatencyMs)> _tokenHealth = new();
    private int _succeededSinceSummary, _rateLimitedSinceSummary, _erroredSinceSummary, _skippedBackoffSinceSummary;
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

    private sealed class TokenWorkState
    {
        public required Token Token;
        public Proxy? Proxy;
        public decimal QuoteAmount;
    }

    // Supervisor swaps this in wholesale each refresh; token loops just read
    // their own entry each iteration — keeps DB load flat (one bulk query a
    // second, same as before) instead of N per-token queries a second.
    private volatile Dictionary<Guid, TokenWorkState> _current = new();

    private readonly ConcurrentDictionary<Guid, (CancellationTokenSource Cts, Task Loop)> _workers = new();

    /// <summary>Logs failures at Warning (visible at default log level) but at
    /// most once per token per minute — this runs per-token every ~1s, so
    /// logging every occurrence would flood the log instead of explaining
    /// anything.</summary>
    private void LogFailureThrottled(Token token, string message, Exception? ex = null)
    {
        Interlocked.Increment(ref _erroredSinceSummary);
        Metrics.JupiterQuoteError.Add(1);
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
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var dbTimer = Stopwatch.StartNew();

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
                dbTimer.Stop();

                var next = new Dictionary<Guid, TokenWorkState>(enabled.Count);
                foreach (var token in enabled)
                {
                    var proxy = token.ProxyId.HasValue && proxyMap.TryGetValue(token.ProxyId.Value, out var p) ? p : null;
                    var quoteAmount = quoteAmounts.GetValueOrDefault(token.Id, 0.01m);
                    next[token.Id] = new TokenWorkState { Token = token, Proxy = proxy, QuoteAmount = quoteAmount };
                }
                _current = next;

                foreach (var id in next.Keys)
                {
                    if (_workers.ContainsKey(id)) continue;
                    var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    _workers[id] = (cts, RunTokenWorkerAsync(id, cts.Token));
                }

                foreach (var id in _workers.Keys)
                {
                    if (next.ContainsKey(id)) continue;
                    if (_workers.TryRemove(id, out var w))
                    {
                        w.Cts.Cancel();
                        w.Cts.Dispose();
                    }
                }

                LogSummaryThrottled(enabled, dbTimer.Elapsed);

                await Task.Delay(SupervisorRefreshMs, stoppingToken);
            }
        }
        finally
        {
            foreach (var w in _workers.Values)
                w.Cts.Cancel();
            try
            {
                await Task.WhenAll(_workers.Values.Select(w => w.Loop));
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
        }
    }

    /// <summary>The independent per-token loop: fetch, wait, repeat, forever
    /// (until this token is disabled/removed and the supervisor cancels it).
    /// Never blocked by any other token's loop.</summary>
    private async Task RunTokenWorkerAsync(Guid tokenId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_current.TryGetValue(tokenId, out var state))
            {
                await FetchTokenQuoteAsync(state.Token, state.Proxy, state.QuoteAmount, ct);
            }

            try
            {
                await Task.Delay(PollIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Once a minute: how many tokens are actually enabled, how many
    /// independent worker loops are running, what happened to them since the
    /// last summary, and which specific tokens are furthest behind on a
    /// fresh price — searchable by symbol instead of having to reason about
    /// the code to guess why one token looks stale.</summary>
    private void LogSummaryThrottled(List<Token> enabled, TimeSpan dbRefreshDuration)
    {
        var now = DateTime.UtcNow;
        if (now - _lastSummaryLogAt < SummaryLogInterval) return;
        _lastSummaryLogAt = now;

        var succeeded = Interlocked.Exchange(ref _succeededSinceSummary, 0);
        var rateLimited = Interlocked.Exchange(ref _rateLimitedSinceSummary, 0);
        var errored = Interlocked.Exchange(ref _erroredSinceSummary, 0);
        var skippedBackoff = Interlocked.Exchange(ref _skippedBackoffSinceSummary, 0);

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
            "Jupiter workers: {Enabled} enabled tokens, {Active} loops running, token-list refresh took {DbMs}ms. Last {IntervalS}s: {Succeeded} ok / {RateLimited} rate-limited / {Errored} errored / {SkippedBackoff} skipped (own backoff). Stalest: {Stalest}",
            enabled.Count, _workers.Count, (int)dbRefreshDuration.TotalMilliseconds, (int)SummaryLogInterval.TotalSeconds,
            succeeded, rateLimited, errored, skippedBackoff,
            string.Join(", ", stale));
    }

    private async Task FetchTokenQuoteAsync(Token token, Proxy? proxy, decimal quoteAmount, CancellationToken ct)
    {
        if (_tokenBackoffUntil.TryGetValue(token.Id, out var backoffUntil) && DateTime.UtcNow < backoffUntil)
        {
            Interlocked.Increment(ref _skippedBackoffSinceSummary);
            Metrics.JupiterQuoteSkippedBackoff.Add(1);
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
                try
                {
                    await FetchAndApplyAsync(token, httpClient, proxy?.Id, url, groupKey, ct);
                }
                catch (Exception ex) when (proxy is not null
                    && token.ProxyFallbackPolicy == ProxyFallbackPolicy.FallbackToSharedIp
                    && !ct.IsCancellationRequested)
                {
                    // TZ п.8.3: only tokens explicitly opted into
                    // FallbackToSharedIp get this — Strict (the default)
                    // stays on ProxyError instead of silently using the
                    // shared IP. This only fires for actual proxy
                    // connectivity failures (exceptions thrown by
                    // GetAsync itself); a 429 or a malformed Jupiter
                    // response means the proxy worked fine, so those are
                    // handled inside FetchAndApplyAsync without throwing.
                    _logger.LogWarning(ex,
                        "Proxy failed for {Symbol} — falling back to shared IP (FallbackToSharedIp policy)",
                        token.Symbol);
                    await FetchAndApplyAsync(token, SharedHttp, null, url, "__shared(fallback)", ct);
                }
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

    /// <summary>Does the actual GET + parse + snapshot write for one quote
    /// attempt via the given client. 429s and malformed Jupiter responses
    /// are handled here and return normally (the proxy itself worked fine in
    /// both cases) — only a genuine exception from GetAsync (connectivity
    /// failure) propagates, which is what triggers the Strict/Fallback
    /// decision in the caller.</summary>
    private async Task FetchAndApplyAsync(Token token, HttpClient httpClient, Guid? proxyId, string url, string groupKey, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var response = await httpClient.GetAsync(url, ct);

        if ((int)response.StatusCode == 429)
        {
            Interlocked.Increment(ref _rateLimitedSinceSummary);
            Metrics.JupiterQuoteRateLimited.Add(1);
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
        snap.ProxyId = proxyId;
        snap.ProxyErrorUntilUtc = 0;
        Interlocked.Increment(ref snap.Sequence);
        Interlocked.Increment(ref _succeededSinceSummary);
        Metrics.JupiterQuoteSuccess.Add(1);
        _tokenHealth[token.Id] = (token.Symbol, DateTime.UtcNow, latencyMs);
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
