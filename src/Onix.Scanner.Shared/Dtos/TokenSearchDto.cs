namespace Onix.Scanner.Shared.Dtos;

public class TokenSearchDto
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string SolanaMint { get; set; } = string.Empty;
    public int Decimals { get; set; }
    public bool IsAvailableOnCex { get; set; }
    public int Popularity { get; set; }
    public decimal? BingxAskPrice { get; set; }
    public decimal? JupiterBuyPrice { get; set; }
    public decimal? SpreadPct { get; set; }
    public TokenHealthStatus? Status { get; set; }
    public DateTime? LastUpdated { get; set; }
}
