namespace Onix.Scanner.Shared.Dtos;

public class TokenCardDto
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public decimal BingxAskPrice { get; set; }
    public decimal JupiterBuyPrice { get; set; }
    public decimal SpreadPct { get; set; }
    public TokenHealthStatus Status { get; set; }
    public DateTime? LastUpdated { get; set; }
    public string BingxUrl { get; set; } = string.Empty;
    public string JupiterUrl { get; set; } = string.Empty;
    public string SolscanUrl { get; set; } = string.Empty;
}
