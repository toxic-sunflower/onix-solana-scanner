using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Core.Contracts;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid userId, CancellationToken ct = default);
    Task<User?> GetByTelegramIdAsync(long telegramId, CancellationToken ct = default);
    Task<User> CreateAsync(User user, CancellationToken ct = default);
    Task UpdateChatIdAsync(Guid userId, long chatId, CancellationToken ct = default);
    Task<int> GetTokenVersionAsync(Guid userId, CancellationToken ct = default);
    Task IncrementTokenVersionAsync(Guid userId, CancellationToken ct = default);
    Task SaveRefreshTokenAsync(RefreshToken token, CancellationToken ct = default);
    Task<RefreshToken?> GetRefreshTokenAsync(string tokenHash, CancellationToken ct = default);
    Task DeleteRefreshTokenAsync(Guid tokenId, CancellationToken ct = default);
    Task DeleteUserRefreshTokensAsync(Guid userId, CancellationToken ct = default);
    Task<List<RefreshToken>> GetSessionsAsync(Guid userId, CancellationToken ct = default);
    Task<RefreshToken?> GetSessionByIdAsync(Guid sessionId, Guid userId, CancellationToken ct = default);
    Task DeleteSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task DeleteOtherSessionsAsync(Guid userId, Guid keepTokenId, CancellationToken ct = default);
    Task UpdateRefreshTokenLastUsedAsync(Guid tokenId, string? ip, CancellationToken ct = default);
    Task<List<UserSubscriber>> GetSubscribersAsync(Guid tokenId, CancellationToken ct = default);
    Task BlacklistJtiAsync(Guid userId, string jti, CancellationToken ct = default);
    Task BlacklistJtisAsync(Guid userId, List<string> jtis, CancellationToken ct = default);
    Task<bool> IsJtiBlacklistedAsync(string jti, CancellationToken ct = default);
    Task CleanupExpiredBlacklistedJtisAsync(CancellationToken ct = default);
}
