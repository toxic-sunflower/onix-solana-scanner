using Microsoft.EntityFrameworkCore;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Infrastructure.Data;

public class ProxyRepository : IProxyRepository
{
    private readonly AppDbContext _db;
    private readonly IEncryptionService _encryption;

    public ProxyRepository(AppDbContext db, IEncryptionService encryption)
    {
        _db = db;
        _encryption = encryption;
    }

    public async Task<List<Proxy>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await _db.Proxies.OrderBy(p => p.Host).ToListAsync(ct);
        foreach (var p in rows)
            if (p.Password is not null)
                p.Password = _encryption.Decrypt(p.Password);
        return rows;
    }

    public async Task<Proxy?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var proxy = await _db.Proxies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (proxy?.Password is not null)
            proxy.Password = _encryption.Decrypt(proxy.Password);
        return proxy;
    }

    public async Task CreateAsync(Proxy proxy, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(proxy.Password))
            proxy.Password = _encryption.Encrypt(proxy.Password);
        _db.Proxies.Add(proxy);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Proxy proxy, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(proxy.Password))
            proxy.Password = _encryption.Encrypt(proxy.Password);
        proxy.UpdatedAt = DateTime.UtcNow;
        _db.Proxies.Update(proxy);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _db.Proxies.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
    }
}
