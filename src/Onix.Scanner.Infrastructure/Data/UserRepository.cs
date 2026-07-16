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
