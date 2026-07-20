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

    public Task<List<Token>> SearchAsync(string? query, bool? cexOnly, CancellationToken ct = default)
    {
        var q = _db.Tokens.AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            q = q.Where(t => t.Symbol.StartsWith(query) || (t.Name != null && t.Name.StartsWith(query)));
        }
        if (cexOnly == true)
            q = q.Where(t => t.IsAvailableOnCex);
        return q.OrderBy(t => t.Symbol).ToListAsync(ct);
    }

    public async Task UpsertBatchAsync(List<Token> tokens, CancellationToken ct = default)
    {
        foreach (var token in tokens)
        {
            var existing = await _db.Tokens.FirstOrDefaultAsync(
                t => t.SolanaMint == token.SolanaMint, ct);
            if (existing is null)
            {
                token.UpdatedAt = DateTime.UtcNow;
                _db.Tokens.Add(token);
            }
            else
            {
                existing.Symbol = token.Symbol;
                existing.Name = token.Name;
                existing.Decimals = token.Decimals;
                existing.BingxSymbol = token.BingxSymbol;
                existing.BingxUrl = token.BingxUrl;
                existing.JupiterInputMint = token.JupiterInputMint;
                existing.JupiterUrl = token.JupiterUrl;
                existing.SolscanUrl = token.SolscanUrl;
                existing.IsAvailableOnCex = token.IsAvailableOnCex;
                existing.Enabled = token.Enabled;
                existing.UpdatedAt = DateTime.UtcNow;
            }
        }
        await _db.SaveChangesAsync(ct);
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

    public async Task<Dictionary<Guid, int>> GetTokenUserCountsAsync(CancellationToken ct = default)
    {
        return await _db.UserTokens
            .GroupBy(ut => ut.TokenId)
            .Select(g => new { TokenId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TokenId, x => x.Count, ct);
    }

    public async Task AddDefaultTokensAsync(Guid userId, CancellationToken ct = default)
    {
        var popular = new[] { "SOL", "BONK", "WIF", "JUP", "PYTH", "RAY", "ORCA", "JTO", "RENDER", "POPCAT" };
        var tokens = await _db.Tokens
            .Where(t => t.Enabled && popular.Contains(t.Symbol))
            .Take(10)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            _db.UserTokens.Add(new UserToken
            {
                UserId = userId,
                TokenId = token.Id,
                QuoteAmount = 100m,
            });
        }
        await _db.SaveChangesAsync(ct);
    }
}
