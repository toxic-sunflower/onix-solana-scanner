using Microsoft.EntityFrameworkCore;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Infrastructure.Data;
using Onix.Scanner.Shared;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Tests.Integration;

public class SpreadTickRepositoryTests : IClassFixture<PostgreSqlFixture>, IDisposable
{
    private readonly ISpreadTickRepository _repo;
    private readonly AppDbContext _db;
    private readonly Guid _tokenId;

    public SpreadTickRepositoryTests(PostgreSqlFixture fixture)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString, x => x.ConfigureDataSource(b =>
                b.DefaultNameTranslator = new Npgsql.NameTranslation.NpgsqlSnakeCaseNameTranslator()))
            .Options;
        _db = new AppDbContext(options);
        _repo = new SpreadTickRepository(_db);
        _tokenId = Guid.NewGuid();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task WriteBatch_And_GetChart()
    {
        var now = DateTime.UtcNow;
        var ticks = new List<SpreadTick>();

        for (var i = 0; i < 10; i++)
        {
            ticks.Add(new SpreadTick
            {
                TokenId = _tokenId,
                BingxAskPrice = 1.0m + i * 0.01m,
                JupiterBuyPrice = 1.0m,
                SpreadPct = i * 1.0m,
                BingxReceivedAt = now.AddSeconds(i),
                JupiterReceivedAt = now.AddSeconds(i),
                CalculatedAt = now.AddSeconds(i),
                BingxLatencyMs = 5,
                JupiterLatencyMs = 10,
                ProxyId = null,
                QualityStatus = QualityStatus.Valid,
            });
        }

        await _repo.WriteBatchAsync(ticks);

        // sleep briefly to ensure data is visible
        await Task.Delay(200);

        var chart = await _repo.GetChartAsync(_tokenId, "5m",
            now.AddSeconds(-1), now.AddSeconds(15));

        Assert.Equal(_tokenId, chart.TokenId);
        Assert.NotNull(chart.Candles);
        Assert.NotEmpty(chart.Candles);
    }

    [Fact]
    public async Task WriteBatch_EmptyList_DoesNothing()
    {
        await _repo.WriteBatchAsync([]);
        // should not throw
    }

    [Fact]
    public async Task Cleanup_RemovesOldTicks()
    {
        var oldTime = DateTime.UtcNow.AddDays(-10);
        var ticks = new List<SpreadTick>
        {
            new()
            {
                TokenId = _tokenId,
                BingxAskPrice = 1.0m,
                JupiterBuyPrice = 1.0m,
                SpreadPct = 0,
                BingxReceivedAt = oldTime,
                JupiterReceivedAt = oldTime,
                CalculatedAt = oldTime,
                QualityStatus = QualityStatus.Valid,
            }
        };

        await _repo.WriteBatchAsync(ticks);
        await _repo.CleanupOldTicksAsync();

        var chart = await _repo.GetChartAsync(_tokenId, "5m",
            oldTime.AddDays(-1), oldTime.AddDays(1));
        Assert.Empty(chart.Candles);
    }
}
