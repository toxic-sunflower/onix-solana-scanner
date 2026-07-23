namespace Onix.Scanner.Shared.Dtos;

public class UserTokenDto
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string SolanaMint { get; set; } = string.Empty;
    public string BingxSymbol { get; set; } = string.Empty;
    public string? BingxUrl { get; set; }
    public string? JupiterUrl { get; set; }
    public string? SolscanUrl { get; set; }
    public decimal BingxAskPrice { get; set; }
    public decimal JupiterBuyPrice { get; set; }
    public decimal SpreadPct { get; set; }
    public bool TelegramEnabled { get; set; } = true;
    public bool IsPinned { get; set; }
    public DateTime? LastUpdated { get; set; }
}
