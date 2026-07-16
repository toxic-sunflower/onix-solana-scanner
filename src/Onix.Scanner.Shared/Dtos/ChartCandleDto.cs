namespace Onix.Scanner.Shared.Dtos;

public class ChartCandleDto
{
    public DateTime Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public int Samples { get; set; }
}

public class ChartResponseDto
{
    public Guid TokenId { get; set; }
    public string Interval { get; set; } = string.Empty;
    public string Timezone { get; set; } = "UTC";
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public List<ChartCandleDto> Candles { get; set; } = [];
}
