namespace Onix.Scanner.Shared.Models;

public class User
{
    public Guid Id { get; set; }
    public long TelegramId { get; set; }
    public string? TelegramUsername { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Language { get; set; }
    public string? AuthToken { get; set; }
    public DateTime? AuthTokenExpiresAt { get; set; }
    public UserRole Role { get; set; }
    public int TokenVersion { get; set; }
    public bool Is2FAEnabled { get; set; }
    public string? TwoFactorSecret { get; set; }
    public string? TwoFactorBackupCodes { get; set; }
    public string? TwoFactorResetCode { get; set; }
    public long? ChatId { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
