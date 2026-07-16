using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Core.Contracts;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid userId, CancellationToken ct = default);
    Task<User?> GetByTelegramIdAsync(long telegramId, CancellationToken ct = default);
    Task<User> CreateAsync(User user, CancellationToken ct = default);
    Task UpdateChatIdAsync(Guid userId, long chatId, CancellationToken ct = default);
    Task<List<UserSubscriber>> GetSubscribersAsync(Guid tokenId, CancellationToken ct = default);
}
