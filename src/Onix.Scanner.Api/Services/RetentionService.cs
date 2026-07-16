using Npgsql;

namespace Onix.Scanner.Api.Services;

public sealed class RetentionService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(IServiceProvider services, ILogger<RetentionService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);

            try
            {
                using var scope = _services.CreateScope();
                var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
                await using var conn = await dataSource.OpenConnectionAsync(stoppingToken);

                await using var cmd1 = conn.CreateCommand();
                cmd1.CommandText = "DELETE FROM spread_ticks WHERE \"CalculatedAt\" < NOW() - INTERVAL '72 hours'";
                var deleted = await cmd1.ExecuteNonQueryAsync(stoppingToken);

                await using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = "DELETE FROM spread_candles WHERE bucket_start < NOW() - INTERVAL '72 hours'";
                var deletedCandles = await cmd2.ExecuteNonQueryAsync(stoppingToken);

                if (deleted > 0 || deletedCandles > 0)
                    _logger.LogInformation("Retention: removed {Ticks} ticks, {Candles} candles >72h old",
                        deleted, deletedCandles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retention cleanup failed");
            }
        }
    }
}
