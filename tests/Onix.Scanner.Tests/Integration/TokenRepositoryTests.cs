using Microsoft.EntityFrameworkCore;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Infrastructure.Data;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Tests.Integration;

public class TokenRepositoryTests : IClassFixture<PostgreSqlFixture>, IDisposable
{
    private readonly ITokenRepository _repo;
    private readonly AppDbContext _db;

    public TokenRepositoryTests(PostgreSqlFixture fixture)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString, x => x.ConfigureDataSource(b =>
                b.DefaultNameTranslator = new Npgsql.NameTranslation.NpgsqlSnakeCaseNameTranslator()))
            .Options;
        _db = new AppDbContext(options);
        _repo = new TokenRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Create_And_GetAll()
    {
        var token = new Token
        {
            Symbol = "TEST",
            Name = "Test Token",
            SolanaMint = "TestMint123456789012345678901234567890",
            BingxSymbol = "TEST-USDT",
            JupiterInputMint = "So11111111111111111111111111111111111111112",
            Decimals = 9,
            BingxUrl = "https://bingx.com/TEST-USDT",
            JupiterUrl = "https://jup.ag/TEST",
            SolscanUrl = "https://solscan.io/token/TEST",
            Enabled = true,
            TelegramEnabled = true,
        };

        var created = await _repo.CreateAsync(token);
        Assert.NotEqual(Guid.Empty, created.Id);

        var all = await _repo.GetAllAsync();
        Assert.Contains(all, t => t.Id == created.Id);

        var byId = await _repo.GetByIdAsync(created.Id);
        Assert.NotNull(byId);
        Assert.Equal("TEST", byId.Symbol);
    }

    [Fact]
    public async Task Update_ModifiesToken()
    {
        var token = new Token
        {
            Symbol = "UPD",
            SolanaMint = "UpdMint1234567890123456789012345678901",
            BingxSymbol = "UPD-USDT",
            JupiterInputMint = "So11111111111111111111111111111111111111112",
            BingxUrl = "https://bingx.com/UPD-USDT",
            JupiterUrl = "https://jup.ag/UPD",
            SolscanUrl = "https://solscan.io/token/UPD",
            Enabled = true,
            TelegramEnabled = false,
        };

        var created = await _repo.CreateAsync(token);

        created.Symbol = "UPDATED";
        created.Enabled = false;
        await _repo.UpdateAsync(created);

        var loaded = await _repo.GetByIdAsync(created.Id);
        Assert.NotNull(loaded);
        Assert.Equal("UPDATED", loaded.Symbol);
        Assert.False(loaded.Enabled);
    }

    [Fact]
    public async Task Delete_RemovesToken()
    {
        var token = new Token
        {
            Symbol = "DEL",
            SolanaMint = "DelMint1234567890123456789012345678901",
            BingxSymbol = "DEL-USDT",
            JupiterInputMint = "So11111111111111111111111111111111111111112",
            BingxUrl = "https://bingx.com/DEL-USDT",
            JupiterUrl = "https://jup.ag/DEL",
            SolscanUrl = "https://solscan.io/token/DEL",
        };

        var created = await _repo.CreateAsync(token);
        await _repo.DeleteAsync(created.Id);

        var loaded = await _repo.GetByIdAsync(created.Id);
        Assert.Null(loaded);
    }
}
