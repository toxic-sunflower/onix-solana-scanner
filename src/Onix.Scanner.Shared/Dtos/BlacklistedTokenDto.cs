namespace Onix.Scanner.Shared.Dtos;

public class BlacklistedTokenDto
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string SolanaMint { get; set; } = string.Empty;
}
