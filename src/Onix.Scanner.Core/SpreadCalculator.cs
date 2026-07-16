using Onix.Scanner.Shared;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Core;

public static class SpreadCalculator
{
    public const int DefaultMaxQuoteAgeMs = 2000;
    public const int DefaultMaxSourceDeltaMs = 2000;
    public const int DefaultStaleThresholdMs = 5000;
    public const decimal DefaultAlertThresholdPct = 5.0m;

    public static decimal CalculateSpread(long bingxRaw, long jupiterRaw)
    {
        if (jupiterRaw == 0) return 0;
        var bingx = (decimal)bingxRaw / 1e18m;
        var jupiter = (decimal)jupiterRaw / 1e18m;
        if (jupiter == 0) return 0;
        return (bingx - jupiter) / jupiter * 100;
    }

    public static TokenHealthStatus ComputeStatus(
        Token token, TokenSnapshot snap, int staleThresholdMs = DefaultStaleThresholdMs)
    {
        if (!token.Enabled) return TokenHealthStatus.Disabled;
        if (string.IsNullOrWhiteSpace(token.SolanaMint)) return TokenHealthStatus.MappingRequired;

        var now = DateTime.UtcNow.Ticks;
        var thresholdTicks = staleThresholdMs * TimeSpan.TicksPerMillisecond;
        var bingxStale = now - Volatile.Read(ref snap.BingxTimestampUtc) > thresholdTicks;
        var jupiterStale = now - Volatile.Read(ref snap.JupiterTimestampUtc) > thresholdTicks;

        if (snap.JupiterBuyPriceRaw == 0 || jupiterStale) return TokenHealthStatus.NoQuote;
        if (bingxStale) return TokenHealthStatus.StaleBingx;
        if (jupiterStale) return TokenHealthStatus.StaleJupiter;

        return TokenHealthStatus.Active;
    }

    public static QualityStatus CalculateQuality(
        long bingxTimestampUtc, long jupiterTimestampUtc,
        int maxQuoteAgeMs = DefaultMaxQuoteAgeMs, int maxSourceDeltaMs = DefaultMaxSourceDeltaMs)
    {
        var now = DateTime.UtcNow.Ticks;
        var staleThreshold = maxQuoteAgeMs * TimeSpan.TicksPerMillisecond;
        var bingxAge = now - Volatile.Read(ref bingxTimestampUtc);
        var jupiterAge = now - Volatile.Read(ref jupiterTimestampUtc);

        var stale = bingxAge > staleThreshold || jupiterAge > staleThreshold;
        var delta = Math.Abs(bingxTimestampUtc - jupiterTimestampUtc);
        var outOfSync = delta > maxSourceDeltaMs * TimeSpan.TicksPerMillisecond;

        return stale || outOfSync ? QualityStatus.Stale : QualityStatus.Valid;
    }
}
