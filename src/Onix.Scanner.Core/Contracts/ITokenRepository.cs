using Onix.Scanner.Shared;
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
    Task<List<Token>> SearchAsync(string? query, bool? cexOnly, CancellationToken ct = default);
    Task UpsertBatchAsync(List<Token> tokens, CancellationToken ct = default);
    Task AddUserTokenAsync(Guid userId, Guid tokenId, CancellationToken ct = default);
    Task RemoveUserTokenAsync(Guid userId, Guid tokenId, CancellationToken ct = default);
    Task<Dictionary<Guid, int>> GetTokenUserCountsAsync(CancellationToken ct = default);
    Task AddDefaultTokensAsync(Guid userId, CancellationToken ct = default);
    Task SetQuoteAmountAsync(Guid tokenId, decimal amount, CancellationToken ct = default);
    Task<decimal?> GetQuoteAmountAsync(Guid tokenId, CancellationToken ct = default);
    Task<Dictionary<Guid, decimal>> GetAllQuoteAmountsAsync(CancellationToken ct = default);
    Task PinTokenAsync(Guid userId, Guid tokenId, bool isPinned, CancellationToken ct = default);
    Task<HashSet<Guid>> GetPinnedTokenIdsAsync(Guid userId, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid tokenId, TokenHealthStatus status, CancellationToken ct = default);
}
