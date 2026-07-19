namespace Onix.Scanner.Shared.Models;

public class Token
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string SolanaMint { get; set; } = string.Empty;
    public string BingxSymbol { get; set; } = string.Empty;
    public string JupiterInputMint { get; set; } = string.Empty;
    public int Decimals { get; set; } = 9;
    public decimal QuoteAmount { get; set; }
    public string BingxUrl { get; set; } = string.Empty;
    public string JupiterUrl { get; set; } = string.Empty;
    public string SolscanUrl { get; set; } = string.Empty;
    public Guid? ProxyId { get; set; }
    public bool Enabled { get; set; } = true;
    public bool TelegramEnabled { get; set; } = true;
    public bool IsAvailableOnCex { get; set; } = false;
    public TokenHealthStatus Status { get; set; } = TokenHealthStatus.Disabled;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
