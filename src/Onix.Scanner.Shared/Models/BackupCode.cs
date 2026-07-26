namespace Onix.Scanner.Shared.Models;

/// <summary>One single-use account-recovery code (TODO.md "Что если потерял
/// Telegram?"). Consumed (deleted) on successful login — no "Used" flag
/// needed. Looked up by hash alone (not scoped by user first) since the
/// recovery form only asks for the code itself, not a username.</summary>
public class BackupCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
