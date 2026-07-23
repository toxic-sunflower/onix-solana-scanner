namespace Onix.Scanner.Shared.Dtos;

public class TokenSearchDto
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string SolanaMint { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public bool IsFavorite { get; set; }
    public int Decimals { get; set; }
    public bool IsAvailableOnCex { get; set; }
    public int Popularity { get; set; }
    public string? BingxSymbol { get; set; }
    public string? BingxUrl { get; set; }
    public string? JupiterUrl { get; set; }
    public string? SolscanUrl { get; set; }
    public decimal? BingxAskPrice { get; set; }
    public decimal? JupiterBuyPrice { get; set; }
    public decimal? SpreadPct { get; set; }
    public TokenHealthStatus? Status { get; set; }
    public DateTime? LastUpdated { get; set; }
}
