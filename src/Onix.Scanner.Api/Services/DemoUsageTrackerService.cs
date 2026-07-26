using Onix.Scanner.Core.Contracts;

namespace Onix.Scanner.Api.Services;

/// <summary>Ticks demo-quota "online" seconds for users who currently have a
/// live SSE connection open (see DemoPolicy) — not wall-clock time since
/// registration. Every TickSeconds, whoever's connected right now gets
/// User.DemoSecondsUsed bumped by TickSeconds; DemoQuotaFilter is what
/// actually blocks requests once a user's total crosses the quota.</summary>
public sealed class DemoUsageTrackerService : BackgroundService
{
    private const int TickSeconds = 30;

    private readonly SseBroadcaster _broadcaster;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DemoUsageTrackerService> _logger;

    public DemoUsageTrackerService(SseBroadcaster broadcaster, IServiceScopeFactory scopeFactory, ILogger<DemoUsageTrackerService> logger)
    {
        _broadcaster = broadcaster;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(TickSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var onlineUserIds = _broadcaster.GetOnlineUserIds();
                if (onlineUserIds.Count == 0) continue;

                using var scope = _scopeFactory.CreateScope();
                var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                await userRepo.IncrementDemoSecondsAsync(onlineUserIds, TickSeconds, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to tick demo usage seconds");
            }
        }
    }
}
