using BenchmarkDotNet.Attributes;
using Onix.Scanner.Core;
using Onix.Scanner.Infrastructure;
using Onix.Scanner.Shared;
using Onix.Scanner.Shared.Dtos;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Tests;

public class PipelineBenchmark
{
    private TokenSnapshotPool _pool = null!;
    private Guid _tokenId;
    private int _snapIdx;

    [GlobalSetup]
    public void Setup()
    {
        _pool = new TokenSnapshotPool();
        _tokenId = Guid.NewGuid();
        _snapIdx = _pool.GetOrAddIndex(_tokenId);

        var now = DateTime.UtcNow.Ticks;
        ref var snap = ref _pool.GetSnapshot(_snapIdx);
        snap.BingxAskPriceRaw = (long)(1.05m * 1e18m);
        snap.JupiterBuyPriceRaw = (long)(1.00m * 1e18m);
        snap.BingxTimestampUtc = now;
        snap.JupiterTimestampUtc = now;
        snap.BingxLatencyMs = 5;
        snap.JupiterLatencyMs = 10;
        snap.ProxyId = null;
    }

    [Benchmark(Baseline = true)]
    public decimal CalculateSpread_Only()
    {
        ref var snap = ref _pool.GetSnapshot(_snapIdx);
        return SpreadCalculator.CalculateSpread(snap.BingxAskPriceRaw, snap.JupiterBuyPriceRaw);
    }

    [Benchmark]
    public decimal FullSpreadPipeline()
    {
        var token = new Token
        {
            Id = _tokenId,
            Symbol = "BENCH",
            SolanaMint = "BenchMint123456789012345678901234567",
            Enabled = true,
            BingxUrl = "https://bingx.com/BENCH",
            JupiterUrl = "https://jup.ag/BENCH",
            SolscanUrl = "https://solscan.io/token/BENCH",
        };

        if (!_pool.TryGetIndex(_tokenId, out var idx))
            return -1;

        ref var snap = ref _pool.GetSnapshot(idx);

        var quality = SpreadCalculator.CalculateQuality(snap.BingxTimestampUtc, snap.JupiterTimestampUtc);
        var spread = SpreadCalculator.CalculateSpread(snap.BingxAskPriceRaw, snap.JupiterBuyPriceRaw);
        var status = SpreadCalculator.ComputeStatus(token, snap);

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

        return spread + dto.Status.ToString().Length + tick.BingxLatencyMs;
    }
}
