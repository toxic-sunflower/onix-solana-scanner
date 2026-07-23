using Onix.Scanner.Core;
using Onix.Scanner.Core.Contracts;

namespace Onix.Scanner.Api.Services;

/// <summary>The token snapshot pool is an in-memory singleton — every process
/// restart (every blue/green deploy) starts it empty, and it only fills back
/// in as BingXConnectorService/JupiterWorkerService poll each token again,
/// which can take tens of seconds. This seeds it from the last persisted
/// spread_tick per token, so prices are already warm the moment the app
/// starts serving traffic instead of a blank dashboard until the live
/// pollers catch up. Registered after MigratorService (needs the schema to
/// exist) and before the pollers (so it doesn't race their live writes).</summary>
public sealed class SnapshotWarmupService : IHostedService
{
    private readonly ITokenSnapshotPool _snapshotPool;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SnapshotWarmupService> _logger;

    public SnapshotWarmupService(
        ITokenSnapshotPool snapshotPool,
        IServiceScopeFactory scopeFactory,
        ILogger<SnapshotWarmupService> logger)
    {
        _snapshotPool = snapshotPool;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var tickRepo = scope.ServiceProvider.GetRequiredService<ISpreadTickRepository>();
            var latestTicks = await tickRepo.GetLatestTicksAsync(ct);

            foreach (var tick in latestTicks)
            {
                var idx = _snapshotPool.GetOrAddIndex(tick.TokenId);
                ref var snap = ref _snapshotPool.GetSnapshot(idx);
                snap.BingxAskPriceRaw = (long)(tick.BingxAskPrice * 1e18m);
                snap.JupiterBuyPriceRaw = (long)(tick.JupiterBuyPrice * 1e18m);
                snap.BingxTimestampUtc = tick.BingxReceivedAt.Ticks;
                snap.JupiterTimestampUtc = tick.JupiterReceivedAt.Ticks;
                Interlocked.Increment(ref snap.Sequence);
            }

            _logger.LogInformation("Warmed up snapshot pool from {Count} persisted ticks", latestTicks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to warm up snapshot pool from persisted ticks — starting cold");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
