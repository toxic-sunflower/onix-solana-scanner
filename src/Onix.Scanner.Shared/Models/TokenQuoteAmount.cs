namespace Onix.Scanner.Shared.Models;

public class TokenQuoteAmount
{
    public Guid TokenId { get; set; }
    public decimal QuoteAmount { get; set; } = 0.01m;
}
