namespace Onix.Scanner.Shared.Models;

public class BlacklistedJti
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Jti { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
