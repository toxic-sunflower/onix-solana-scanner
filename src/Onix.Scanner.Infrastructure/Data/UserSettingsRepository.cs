using Microsoft.EntityFrameworkCore;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Infrastructure.Data;

public class UserSettingsRepository : IUserSettingsRepository
{
    private readonly AppDbContext _db;

    public UserSettingsRepository(AppDbContext db) => _db = db;

    public async Task<UserSettings?> GetByTelegramIdAsync(long telegramId, CancellationToken ct = default) =>
        await _db.UserSettings.FirstOrDefaultAsync(s => s.TelegramId == telegramId, ct);

    public async Task UpsertAsync(UserSettings settings, CancellationToken ct = default)
    {
        var existing = await _db.UserSettings.FirstOrDefaultAsync(s => s.TelegramId == settings.TelegramId, ct);
        if (existing is not null)
        {
            existing.MinimalSpreadPct = settings.MinimalSpreadPct;
            existing.TelegramNotificationsEnabled = settings.TelegramNotificationsEnabled;
            existing.CooldownSeconds = settings.CooldownSeconds;
            existing.Timezone = settings.Timezone;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            settings.Id = Guid.NewGuid();
            settings.CreatedAt = DateTime.UtcNow;
            settings.UpdatedAt = DateTime.UtcNow;
            _db.UserSettings.Add(settings);
        }
        await _db.SaveChangesAsync(ct);
    }
}
