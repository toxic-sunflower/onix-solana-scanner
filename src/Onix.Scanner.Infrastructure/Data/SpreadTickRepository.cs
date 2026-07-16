using Microsoft.EntityFrameworkCore;
using Npgsql;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Dtos;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Infrastructure.Data;

public class SpreadTickRepository : ISpreadTickRepository
{
    private readonly AppDbContext _db;

    public SpreadTickRepository(AppDbContext db) => _db = db;

    public async Task WriteBatchAsync(IReadOnlyList<SpreadTick> ticks, CancellationToken ct = default)
    {
        if (ticks.Count == 0) return;
        _db.SpreadTicks.AddRange(ticks);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ChartResponseDto> GetChartAsync(Guid tokenId, string interval, DateTime from,
        DateTime to, string timezone = "UTC", CancellationToken ct = default)
    {
        var bucketSeconds = interval switch
        {
            "5m" => 300,
            "15m" => 900,
            "1h" => 3600,
            _ => 300
        };

        var conn = _db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            WITH buckets AS (
                SELECT to_timestamp(
                    floor(extract(epoch FROM "CalculatedAt") / :bs) * :bs
                ) AT TIME ZONE 'UTC' AS bucket_start,
                    (array_agg("SpreadPct" ORDER BY "CalculatedAt"))[1] AS open,
                    MAX("SpreadPct") AS high,
                    MIN("SpreadPct") AS low,
                    (array_agg("SpreadPct" ORDER BY "CalculatedAt" DESC))[1] AS close,
                    COUNT(*) AS samples
                FROM spread_ticks
                WHERE "TokenId" = @id
                    AND "CalculatedAt" >= @from
                    AND "CalculatedAt" < @to
                    AND "QualityStatus" = 'Valid'
                GROUP BY bucket_start
                ORDER BY bucket_start
            )
            SELECT bucket_start AT TIME ZONE 'UTC' AS "Time", open AS "Open",
                high AS "High", low AS "Low", close AS "Close", samples AS "Samples"
            FROM buckets
            """;
        cmd.Parameters.Add(new NpgsqlParameter("bs", bucketSeconds));
        cmd.Parameters.Add(new NpgsqlParameter("id", tokenId));
        cmd.Parameters.Add(new NpgsqlParameter("from", from));
        cmd.Parameters.Add(new NpgsqlParameter("to", to));

        var candles = new List<ChartCandleDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            candles.Add(new ChartCandleDto
            {
                Time = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc),
                Open = reader.GetDecimal(1),
                High = reader.GetDecimal(2),
                Low = reader.GetDecimal(3),
                Close = reader.GetDecimal(4),
                Samples = reader.GetInt32(5),
            });
        }

        return new ChartResponseDto
        {
            TokenId = tokenId,
            Interval = interval,
            Timezone = timezone,
            From = from,
            To = to,
            Candles = candles
        };
    }

    public async Task CleanupOldTicksAsync(CancellationToken ct = default)
    {
        await _db.SpreadTicks
            .Where(t => t.CalculatedAt < DateTime.UtcNow.AddHours(-72))
            .ExecuteDeleteAsync(ct);
    }
}
