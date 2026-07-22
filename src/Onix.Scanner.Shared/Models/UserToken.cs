namespace Onix.Scanner.Shared.Models;

public class UserToken
{
    public Guid UserId { get; set; }
    public Guid TokenId { get; set; }
    public bool TelegramEnabled { get; set; } = true;
    public decimal AlertThresholdPct { get; set; } = 5m;
    public bool IsPinned { get; set; }
    public DateTime? LastSignalAt { get; set; }
    public bool IsArmed { get; set; } = true;
}
