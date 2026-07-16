using Microsoft.EntityFrameworkCore;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Infrastructure.Data;
using Onix.Scanner.Infrastructure.Services;
using Onix.Scanner.Shared;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Tests.Integration;

public class ProxyRepositoryTests : IClassFixture<PostgreSqlFixture>, IDisposable
{
    private readonly IProxyRepository _repo;
    private readonly AppDbContext _db;

    public ProxyRepositoryTests(PostgreSqlFixture fixture)
    {
        var key = "0123456789abcdef"u8.ToArray();
        var encryption = new AesEncryptionService(key);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString, x => x.ConfigureDataSource(b =>
                b.DefaultNameTranslator = new Npgsql.NameTranslation.NpgsqlSnakeCaseNameTranslator()))
            .Options;
        _db = new AppDbContext(options);
        _repo = new ProxyRepository(_db, encryption);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Create_And_GetAll_WithEncryptedPassword()
    {
        var proxy = new Proxy
        {
            Id = Guid.NewGuid(),
            Type = "HTTP",
            Host = "192.168.1.1",
            Port = 8080,
            Username = "user123",
            Password = "secret!pass",
            Enabled = true,
            Status = ProxyStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await _repo.CreateAsync(proxy);

        var all = await _repo.GetAllAsync();
        var loaded = all.FirstOrDefault(p => p.Id == proxy.Id);
        Assert.NotNull(loaded);
        Assert.Equal("secret!pass", loaded.Password);
        Assert.Equal(8080, loaded.Port);
    }

    [Fact]
    public async Task Update_ChangesPassword()
    {
        var proxy = new Proxy
        {
            Id = Guid.NewGuid(),
            Type = "HTTP",
            Host = "10.0.0.1",
            Port = 3128,
            Username = "admin",
            Password = "old_pass",
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await _repo.CreateAsync(proxy);

        proxy.Password = "new_pass";
        proxy.Port = 9999;
        await _repo.UpdateAsync(proxy);

        var loaded = await _repo.GetByIdAsync(proxy.Id);
        Assert.NotNull(loaded);
        Assert.Equal("new_pass", loaded.Password);
        Assert.Equal(9999, loaded.Port);
    }

    [Fact]
    public async Task Delete_RemovesProxy()
    {
        var proxy = new Proxy
        {
            Id = Guid.NewGuid(),
            Type = "HTTP",
            Host = "172.16.0.1",
            Port = 8888,
            Enabled = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await _repo.CreateAsync(proxy);
        await _repo.DeleteAsync(proxy.Id);

        var loaded = await _repo.GetByIdAsync(proxy.Id);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task Password_StoredEncrypted_RoundtripSucceeds()
    {
        var proxy = new Proxy
        {
            Id = Guid.NewGuid(),
            Type = "HTTP",
            Host = "10.10.10.10",
            Port = 80,
            Password = "super_secret",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await _repo.CreateAsync(proxy);
        var loaded = await _repo.GetByIdAsync(proxy.Id);
        Assert.NotNull(loaded);
        Assert.Equal("super_secret", loaded.Password);
    }
}
