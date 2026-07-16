using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Core.Contracts;

public interface IProxyRepository
{
    Task<List<Proxy>> GetAllAsync(CancellationToken ct = default);
    Task<Proxy?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(Proxy proxy, CancellationToken ct = default);
    Task UpdateAsync(Proxy proxy, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
