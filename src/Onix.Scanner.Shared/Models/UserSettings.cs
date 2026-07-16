namespace Onix.Scanner.Shared.Models;

public class UserSettings
{
    public Guid Id { get; set; }
    public long? TelegramId { get; set; }
    public decimal MinimalSpreadPct { get; set; } = 5.0m;
    public bool TelegramNotificationsEnabled { get; set; } = true;
    public int CooldownSeconds { get; set; } = 300;
    public string Timezone { get; set; } = "UTC";
    public UserRole Role { get; set; } = UserRole.User;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
