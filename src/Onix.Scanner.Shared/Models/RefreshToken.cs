namespace Onix.Scanner.Shared.Models;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string? IpAddress { get; set; }
    public string? LastJti { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public string DisplayName =>
        !string.IsNullOrEmpty(DeviceName) ? DeviceName :
        !string.IsNullOrEmpty(IpAddress) ? IpAddress :
        "Unknown device";
}
