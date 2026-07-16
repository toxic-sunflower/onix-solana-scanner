using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Core.Contracts;

public interface IUserSettingsRepository
{
    Task<UserSettings?> GetByTelegramIdAsync(long telegramId, CancellationToken ct = default);
    Task UpsertAsync(UserSettings settings, CancellationToken ct = default);
}
