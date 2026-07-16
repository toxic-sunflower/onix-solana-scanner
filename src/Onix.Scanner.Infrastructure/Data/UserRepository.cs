using Microsoft.EntityFrameworkCore;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Infrastructure.Data;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;

    public UserRepository(AppDbContext db) => _db = db;

    public Task<User?> GetByIdAsync(Guid userId, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

    public Task<User?> GetByTelegramIdAsync(long telegramId, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId, ct);

    public async Task<User> CreateAsync(User user, CancellationToken ct = default)
    {
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateChatIdAsync(Guid userId, long chatId, CancellationToken ct = default)
    {
        await _db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.ChatId, chatId)
                .SetProperty(u => u.UpdatedAt, DateTime.UtcNow), ct);
    }

    public async Task<int> GetTokenVersionAsync(Guid userId, CancellationToken ct = default)
    {
        var version = await _db.Users.Where(u => u.Id == userId)
            .Select(u => (int?)u.TokenVersion)
            .FirstOrDefaultAsync(ct);
        return version ?? 0;
    }

    public async Task IncrementTokenVersionAsync(Guid userId, CancellationToken ct = default)
    {
        await _db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1)
                .SetProperty(u => u.UpdatedAt, DateTime.UtcNow), ct);
    }

    public async Task SaveRefreshTokenAsync(RefreshToken token, CancellationToken ct = default)
    {
        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync(ct);
    }

    public Task<RefreshToken?> GetRefreshTokenAsync(string tokenHash, CancellationToken ct = default) =>
        _db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == tokenHash && r.ExpiresAt > DateTime.UtcNow, ct);

    public async Task DeleteRefreshTokenAsync(Guid tokenId, CancellationToken ct = default)
    {
        await _db.RefreshTokens.Where(r => r.Id == tokenId).ExecuteDeleteAsync(ct);
    }

    public async Task DeleteUserRefreshTokensAsync(Guid userId, CancellationToken ct = default)
    {
        await _db.RefreshTokens.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);
    }

    public Task<List<RefreshToken>> GetSessionsAsync(Guid userId, CancellationToken ct = default) =>
        _db.RefreshTokens.Where(r => r.UserId == userId && r.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(r => r.LastUsedAt ?? r.CreatedAt)
            .ToListAsync(ct);

    public Task<RefreshToken?> GetSessionByIdAsync(Guid sessionId, Guid userId, CancellationToken ct = default) =>
        _db.RefreshTokens.FirstOrDefaultAsync(r => r.Id == sessionId && r.UserId == userId, ct);

    public async Task DeleteSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        await _db.RefreshTokens.Where(r => r.Id == sessionId).ExecuteDeleteAsync(ct);
    }

    public async Task UpdateRefreshTokenLastUsedAsync(Guid tokenId, string? ip, CancellationToken ct = default)
    {
        await _db.RefreshTokens.Where(r => r.Id == tokenId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.LastUsedAt, DateTime.UtcNow)
                .SetProperty(r => r.IpAddress, ip), ct);
    }

    public async Task DeleteOtherSessionsAsync(Guid userId, Guid keepTokenId, CancellationToken ct = default)
    {
        await _db.RefreshTokens.Where(r => r.UserId == userId && r.Id != keepTokenId).ExecuteDeleteAsync(ct);
    }

    public async Task<List<UserSubscriber>> GetSubscribersAsync(Guid tokenId, CancellationToken ct = default)
    {
        return await (from u in _db.Users
                      join ut in _db.UserTokens on u.Id equals ut.UserId
                      join up in _db.UserPreferences on u.Id equals up.UserId into upJoin
                      from up in upJoin.DefaultIfEmpty()
                      where ut.TokenId == tokenId && ut.TelegramEnabled && u.ChatId != null
                      select new UserSubscriber
                      {
                          UserId = u.Id,
                          ChatId = (long)u.ChatId,
                          AlertThresholdPct = ut.AlertThresholdPct,
                          CooldownSeconds = up == null ? 300 : up.CooldownSeconds
                      }).ToListAsync(ct);
    }
}
