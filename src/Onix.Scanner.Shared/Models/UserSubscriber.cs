namespace Onix.Scanner.Shared.Models;

public class UserSubscriber
{
    public Guid UserId { get; set; }
    public long ChatId { get; set; }
    public decimal AlertThresholdPct { get; set; }
    public int CooldownSeconds { get; set; }
    public DateTime? LastSignalAt { get; set; }
    public bool IsArmed { get; set; }
}
