using Microsoft.EntityFrameworkCore;
using Onix.Scanner.Infrastructure.Data;

namespace Onix.Scanner.Api.Services;

public sealed class MigratorService : IHostedService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<MigratorService> _logger;

    public MigratorService(IDbContextFactory<AppDbContext> dbFactory, ILogger<MigratorService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                await db.Database.MigrateAsync(ct);

                await db.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE IF NOT EXISTS spread_candles (
                        bucket_start timestamptz NOT NULL,
                        token_id uuid NOT NULL,
                        interval_seconds int NOT NULL,
                        open numeric(20,10),
                        high numeric(20,10),
                        low numeric(20,10),
                        close numeric(20,10),
                        samples int NOT NULL DEFAULT 0,
                        avg_spread numeric(20,10),
                        PRIMARY KEY (bucket_start, token_id, interval_seconds)
                    );
                    """, ct);

                _logger.LogInformation("Migrations applied successfully");
                return;
            }
            catch (Exception ex) when (attempt < 10)
            {
                _logger.LogWarning(ex, "Migration attempt {A}/10 failed, retrying in 3s", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "All 10 migration attempts failed — refusing to start with a mismatched schema");
                throw;
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
