namespace Onix.Scanner.Shared.Models;

public class SpreadTick
{
    public long Id { get; set; }
    public Guid TokenId { get; set; }
    public decimal BingxAskPrice { get; set; }
    public decimal JupiterBuyPrice { get; set; }
    public decimal SpreadPct { get; set; }
    public DateTime BingxReceivedAt { get; set; }
    public DateTime JupiterReceivedAt { get; set; }
    public DateTime CalculatedAt { get; set; }
    public int BingxLatencyMs { get; set; }
    public int JupiterLatencyMs { get; set; }
    public Guid? ProxyId { get; set; }
    public QualityStatus QualityStatus { get; set; } = QualityStatus.Valid;
}
