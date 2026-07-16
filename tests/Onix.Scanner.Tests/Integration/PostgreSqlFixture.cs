using Microsoft.EntityFrameworkCore;
using Npgsql;
using Onix.Scanner.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace Onix.Scanner.Tests.Integration;

public class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("onix_scanner")
        .WithUsername("onix")
        .WithPassword("onix_test_pass")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString, x => x.ConfigureDataSource(b =>
                b.DefaultNameTranslator = new Npgsql.NameTranslation.NpgsqlSnakeCaseNameTranslator()))
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
