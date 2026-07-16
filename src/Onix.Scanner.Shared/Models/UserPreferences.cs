namespace Onix.Scanner.Shared.Models;

public class UserPreferences
{
    public Guid UserId { get; set; }
    public decimal MinimalSpreadPct { get; set; } = 5m;
    public int CooldownSeconds { get; set; } = 300;
    public string Timezone { get; set; } = "UTC";
    public DateTime UpdatedAt { get; set; }
}
