using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Core.Contracts;

public interface ITokenRepository
{
    Task<List<Token>> GetAllAsync(CancellationToken ct = default);
    Task<Token?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Token?> GetBySymbolAsync(string symbol, CancellationToken ct = default);
    Task<Token> CreateAsync(Token token, CancellationToken ct = default);
    Task UpdateAsync(Token token, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Token?> GetByMintAsync(string solanaMint, CancellationToken ct = default);
    Task<List<Token>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddUserTokenAsync(Guid userId, Guid tokenId, CancellationToken ct = default);
    Task RemoveUserTokenAsync(Guid userId, Guid tokenId, CancellationToken ct = default);
}
