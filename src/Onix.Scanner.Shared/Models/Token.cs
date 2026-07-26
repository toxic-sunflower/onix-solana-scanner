namespace Onix.Scanner.Shared.Models;

public class Token
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string SolanaMint { get; set; } = string.Empty;
    public string BingxSymbol { get; set; } = string.Empty;
    public string JupiterInputMint { get; set; } = string.Empty;
    public int JupiterInputDecimals { get; set; } = 6;
    public int Decimals { get; set; } = 9;
    public string BingxUrl { get; set; } = string.Empty;
    public string JupiterUrl { get; set; } = string.Empty;
    public string SolscanUrl { get; set; } = string.Empty;
    public Guid? ProxyId { get; set; }

    /// <summary>TZ п.8.3: what happens if this token's assigned proxy fails.
    /// Strict = stay on ProxyError, never silently use the shared IP.
    /// FallbackToSharedIp = retry once via the shared IP on proxy failure.
    /// Meaningless when ProxyId is null (nothing to fall back FROM).</summary>
    public ProxyFallbackPolicy ProxyFallbackPolicy { get; set; } = ProxyFallbackPolicy.Strict;
    public bool Enabled { get; set; } = true;

    /// <summary>TZ п.5: при конфликте/неоднозначности тикера между Jupiter и
    /// BingX токен не должен запускаться автоматически. Also covers the
    /// broader case a literal in-batch collision check would miss: a single
    /// unambiguous Jupiter match can still be the wrong project entirely
    /// (see the AVA incident — CEX $0.16 vs DEX $0.008, ~20x apart). Every
    /// newly-discovered token starts with this true and Enabled false;
    /// admin confirms or rejects via the admin panel. Once set on an
    /// existing row, TokenSyncService resync must never flip it back on its
    /// own (see TokenRepository.UpsertBatchAsync).</summary>
    public bool RequiresMapping { get; set; }
    public bool TelegramEnabled { get; set; } = true;
    public bool IsAvailableOnCex { get; set; } = false;
    public TokenHealthStatus Status { get; set; } = TokenHealthStatus.Disabled;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
