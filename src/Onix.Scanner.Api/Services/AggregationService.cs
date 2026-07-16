using Npgsql;

namespace Onix.Scanner.Api.Services;

public sealed class AggregationService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AggregationService> _logger;

    private static readonly (string Interval, int Seconds)[] Intervals =
        [("5m", 300), ("15m", 900), ("1h", 3600)];

    public AggregationService(IServiceProvider services, ILogger<AggregationService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(60_000, stoppingToken);

            try
            {
                await AggregateAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Aggregation failed");
            }
        }
    }

    private async Task AggregateAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        foreach (var (interval, seconds) in Intervals)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO spread_candles (bucket_start, token_id, interval_seconds, open, high, low, close, samples, avg_spread)
                SELECT
                    time_bucket(:interval::interval, "CalculatedAt") AS bucket_start,
                    "TokenId",
                    :seconds AS interval_seconds,
                    FIRST("SpreadPct", "CalculatedAt") AS open,
                    MAX("SpreadPct") AS high,
                    MIN("SpreadPct") AS low,
                    LAST("SpreadPct", "CalculatedAt") AS close,
                    COUNT(*) AS samples,
                    AVG("SpreadPct") AS avg_spread
                FROM spread_ticks
                WHERE "CalculatedAt" >= NOW() - :interval::interval
                    AND "QualityStatus" = 'Valid'
                GROUP BY "TokenId", bucket_start
                ON CONFLICT (bucket_start, "TokenId", interval_seconds)
                DO UPDATE SET
                    open = EXCLUDED.open,
                    high = EXCLUDED.high,
                    low = EXCLUDED.low,
                    close = EXCLUDED.close,
                    samples = EXCLUDED.samples,
                    avg_spread = EXCLUDED.avg_spread
                """;
            cmd.Parameters.Add(new NpgsqlParameter("interval", $"{seconds} seconds"));
            cmd.Parameters.Add(new NpgsqlParameter("seconds", seconds));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        _logger.LogDebug("Aggregation completed for {Count} intervals", Intervals.Length);
    }
}
