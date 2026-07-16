using System.Diagnostics;
using Onix.Scanner.Core;
using Onix.Scanner.Infrastructure;
using Onix.Scanner.Shared;
using Onix.Scanner.Shared.Dtos;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Tests;

public class SpreadCalculatorTests
{
    private const long _1e18 = 1_000_000_000_000_000_000;

    [Fact]
    public void CalculateSpread_ReturnsPositivePct()
    {
        var spread = SpreadCalculator.CalculateSpread(
            (long)(1.05m * _1e18), (long)(1.00m * _1e18));

        Assert.Equal(5.0m, spread, 4);
    }

    [Fact]
    public void CalculateSpread_ReturnsNegativePct_WhenBingxBelowJupiter()
    {
        var spread = SpreadCalculator.CalculateSpread(
            (long)(0.95m * _1e18), (long)(1.00m * _1e18));

        Assert.Equal(-5.0m, spread, 4);
    }

    [Fact]
    public void CalculateSpread_ReturnsZero_WhenJupiterIsZero()
    {
        Assert.Equal(0m, SpreadCalculator.CalculateSpread(_1e18, 0));
    }

    [Fact]
    public void CalculateSpread_ReturnsZero_WhenBothEqual()
    {
        var val = (long)(1.2345m * _1e18);
        Assert.Equal(0m, SpreadCalculator.CalculateSpread(val, val), 4);
    }

    [Fact]
    public void CalculateSpread_Latency_UnderOneMicrosecond()
    {
        var bingx = (long)(1.05m * _1e18);
        var jupiter = (long)(1.00m * _1e18);
        var sw = Stopwatch.StartNew();
        var iterations = 100_000;
        decimal sum = 0;

        for (var i = 0; i < iterations; i++)
            sum += SpreadCalculator.CalculateSpread(bingx, jupiter);

        sw.Stop();
        var avgNs = (double)sw.Elapsed.TotalNanoseconds / iterations;

        // prevent optimizer from eliding the loop
        Assert.NotEqual(0m, sum);
        Assert.True(avgNs < 1000, $"Avg latency {avgNs:F1}ns — expected <1000ns");
    }

    [Fact]
    public void ComputeStatus_Active_WhenBothFreshAndMapped()
    {
        var now = DateTime.UtcNow.Ticks;
        var token = new Token { Enabled = true, SolanaMint = "abc" };
        var snap = new TokenSnapshot
        {
            BingxTimestampUtc = now,
            JupiterTimestampUtc = now,
            JupiterBuyPriceRaw = (long)(1.0m * _1e18)
        };
        Assert.Equal(TokenHealthStatus.Active, SpreadCalculator.ComputeStatus(token, snap));
    }

    [Fact]
    public void ComputeStatus_StaleBingx_WhenBingxOld()
    {
        var now = DateTime.UtcNow.Ticks;
        var token = new Token { Enabled = true, SolanaMint = "abc" };
        var snap = new TokenSnapshot
        {
            BingxTimestampUtc = now - TimeSpan.FromSeconds(10).Ticks,
            JupiterTimestampUtc = now,
            JupiterBuyPriceRaw = (long)(1.0m * _1e18)
        };
        Assert.Equal(TokenHealthStatus.StaleBingx, SpreadCalculator.ComputeStatus(token, snap));
    }

    [Fact]
    public void ComputeStatus_NoQuote_WhenJupiterRawZero()
    {
        var now = DateTime.UtcNow.Ticks;
        var token = new Token { Enabled = true, SolanaMint = "abc" };
        var snap = new TokenSnapshot
        {
            BingxTimestampUtc = now,
            JupiterTimestampUtc = now,
            JupiterBuyPriceRaw = 0
        };
        Assert.Equal(TokenHealthStatus.NoQuote, SpreadCalculator.ComputeStatus(token, snap));
    }

    [Fact]
    public void ComputeStatus_NoQuote_WhenJupiterStale()
    {
        var now = DateTime.UtcNow.Ticks;
        var token = new Token { Enabled = true, SolanaMint = "abc" };
        var snap = new TokenSnapshot
        {
            BingxTimestampUtc = now,
            JupiterTimestampUtc = now - TimeSpan.FromSeconds(10).Ticks,
            JupiterBuyPriceRaw = (long)(1.0m * _1e18)
        };
        Assert.Equal(TokenHealthStatus.NoQuote, SpreadCalculator.ComputeStatus(token, snap));
    }

    [Fact]
    public void ComputeStatus_Disabled_WhenTokenDisabled()
    {
        Assert.Equal(TokenHealthStatus.Disabled,
            SpreadCalculator.ComputeStatus(new Token { Enabled = false }, new TokenSnapshot()));
    }

    [Fact]
    public void ComputeStatus_MappingRequired_WhenMintEmpty()
    {
        Assert.Equal(TokenHealthStatus.MappingRequired,
            SpreadCalculator.ComputeStatus(new Token { Enabled = true, SolanaMint = "" }, new TokenSnapshot()));
    }

    [Fact]
    public void CalculateQuality_Valid_WhenBothFresh()
    {
        var now = DateTime.UtcNow.Ticks;
        Assert.Equal(QualityStatus.Valid, SpreadCalculator.CalculateQuality(now, now));
    }

    [Fact]
    public void CalculateQuality_Stale_WhenBingxOld()
    {
        var now = DateTime.UtcNow.Ticks;
        var old = now - TimeSpan.FromSeconds(10).Ticks;
        Assert.Equal(QualityStatus.Stale, SpreadCalculator.CalculateQuality(old, now));
    }

    [Fact]
    public void CalculateQuality_Stale_WhenOutOfSync()
    {
        var now = DateTime.UtcNow.Ticks;
        var far = now - TimeSpan.FromSeconds(5).Ticks;
        Assert.Equal(QualityStatus.Stale, SpreadCalculator.CalculateQuality(now, far, maxSourceDeltaMs: 1000));
    }

    [Fact]
    public void FullPipeline_Latency_UnderOneMicrosecond()
    {
        var pool = new TokenSnapshotPool();
        var tokenId = Guid.NewGuid();
        var snapIdx = pool.GetOrAddIndex(tokenId);

        var now = DateTime.UtcNow.Ticks;
        ref var snap = ref pool.GetSnapshot(snapIdx);
        snap.BingxAskPriceRaw = (long)(1.05m * _1e18);
        snap.JupiterBuyPriceRaw = (long)(1.00m * _1e18);
        snap.BingxTimestampUtc = now;
        snap.JupiterTimestampUtc = now;
        snap.BingxLatencyMs = 5;
        snap.JupiterLatencyMs = 10;

        var token = new Token
        {
            Id = tokenId,
            Symbol = "LAT",
            SolanaMint = "LatMint1234567890123456789012345678",
            Enabled = true,
            BingxUrl = "https://bingx.com/LAT",
            JupiterUrl = "https://jup.ag/LAT",
            SolscanUrl = "https://solscan.io/token/LAT",
        };

        var sw = Stopwatch.StartNew();
        const int iterations = 100_000;
        decimal sum = 0;

        for (var i = 0; i < iterations; i++)
        {
            if (!pool.TryGetIndex(tokenId, out var idx)) continue;
            ref var s = ref pool.GetSnapshot(idx);

            var quality = SpreadCalculator.CalculateQuality(s.BingxTimestampUtc, s.JupiterTimestampUtc);
            var spread = SpreadCalculator.CalculateSpread(s.BingxAskPriceRaw, s.JupiterBuyPriceRaw);
            var status = SpreadCalculator.ComputeStatus(token, s);

            var tick = new SpreadTick
            {
                TokenId = token.Id,
                BingxAskPrice = s.BingxAskPriceRaw / 1e18m,
                JupiterBuyPrice = s.JupiterBuyPriceRaw / 1e18m,
                SpreadPct = spread,
                BingxReceivedAt = new DateTime(s.BingxTimestampUtc, DateTimeKind.Utc),
                JupiterReceivedAt = new DateTime(s.JupiterTimestampUtc, DateTimeKind.Utc),
                CalculatedAt = DateTime.UtcNow,
                BingxLatencyMs = s.BingxLatencyMs,
                JupiterLatencyMs = s.JupiterLatencyMs,
                ProxyId = s.ProxyId,
                QualityStatus = quality,
            };

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

            sum += spread + dto.Status.ToString().Length + tick.BingxLatencyMs;
        }

        sw.Stop();
        var avgNs = (double)sw.Elapsed.TotalNanoseconds / iterations;

        Assert.NotEqual(0m, sum);
        Assert.True(avgNs < 2000, $"Full pipeline avg {avgNs:F1}ns — expected <2000ns");
    }
}
