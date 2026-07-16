namespace Onix.Scanner.Shared.Models;

public class Proxy
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "HTTP";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? LastCheckAt { get; set; }
    public ProxyStatus Status { get; set; } = ProxyStatus.Disabled;
    public int? LatencyMs { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
