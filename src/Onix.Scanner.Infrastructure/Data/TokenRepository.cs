using Microsoft.EntityFrameworkCore;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Infrastructure.Data;

public class TokenRepository : ITokenRepository
{
    private readonly AppDbContext _db;

    public TokenRepository(AppDbContext db) => _db = db;

    public Task<List<Token>> GetAllAsync(CancellationToken ct = default) =>
        _db.Tokens.OrderBy(t => t.Symbol).ToListAsync(ct);

    public Task<Token?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Tokens.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<Token?> GetBySymbolAsync(string symbol, CancellationToken ct = default) =>
        _db.Tokens.FirstOrDefaultAsync(t => t.Symbol == symbol, ct);

    public Task<Token?> GetByMintAsync(string solanaMint, CancellationToken ct = default) =>
        _db.Tokens.FirstOrDefaultAsync(t => t.SolanaMint == solanaMint, ct);

    public Task<List<Token>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        (from t in _db.Tokens
         join ut in _db.UserTokens on t.Id equals ut.TokenId
         where ut.UserId == userId
         orderby t.Symbol
         select t).ToListAsync(ct);

    public async Task<Token> CreateAsync(Token token, CancellationToken ct = default)
    {
        _db.Tokens.Add(token);
        await _db.SaveChangesAsync(ct);
        return token;
    }

    public async Task UpdateAsync(Token token, CancellationToken ct = default)
    {
        token.UpdatedAt = DateTime.UtcNow;
        _db.Tokens.Update(token);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _db.Tokens.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task AddUserTokenAsync(Guid userId, Guid tokenId, CancellationToken ct = default)
    {
        try
        {
            _db.UserTokens.Add(new UserToken { UserId = userId, TokenId = tokenId });
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) { }
    }

    public async Task RemoveUserTokenAsync(Guid userId, Guid tokenId, CancellationToken ct = default)
    {
        await _db.UserTokens
            .Where(ut => ut.UserId == userId && ut.TokenId == tokenId)
            .ExecuteDeleteAsync(ct);
    }
}
