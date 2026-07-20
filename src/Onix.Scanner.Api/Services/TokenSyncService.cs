using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Api.Services;

public sealed class TokenSyncService : BackgroundService
{
    private readonly ILogger<TokenSyncService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;

    private static readonly HashSet<string> Stablecoins =
        new(StringComparer.OrdinalIgnoreCase) { "USDC", "USDT", "DAI", "FDUSD", "PYUSD", "USDE", "BUSD", "TUSD", "LUSD" };

    public TokenSyncService(
        ILogger<TokenSyncService> logger,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SyncTokensAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token sync failed on startup, will retry later");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SyncTokensAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scheduled token sync failed");
            }
        }
    }

    private async Task SyncTokensAsync(CancellationToken ct)
    {
        _logger.LogInformation("Syncing token list from Jupiter + BingX");

        var jupTokens = await FetchJupiterTokensAsync(ct);
        if (jupTokens.Count == 0)
        {
            _logger.LogWarning("No tokens from Jupiter API, skipping sync");
            return;
        }
        var bingxSymbols = await FetchBingxPairsAsync(ct);

        var tokens = new List<Token>(jupTokens.Count);
        foreach (var jt in jupTokens)
        {
            if (Stablecoins.Contains(jt.Symbol)) continue;

            var cex = bingxSymbols.Contains(jt.Symbol);
            var bingxSymbol = cex ? $"{jt.Symbol.ToUpperInvariant()}-USDT" : "";

            tokens.Add(new Token
            {
                Symbol = jt.Symbol,
                Name = jt.Name,
                SolanaMint = jt.Mint,
                Decimals = jt.Decimals,
                BingxSymbol = bingxSymbol,
                JupiterInputMint = jt.Mint,
                BingxUrl = cex ? $"https://www.bingx.com/en-us/futures/{bingxSymbol}" : "",
                JupiterUrl = $"https://jup.ag/swap/{jt.Symbol}-USDC",
                SolscanUrl = $"https://solscan.io/token/{jt.Mint}",
                Enabled = cex,
                TelegramEnabled = false,
                IsAvailableOnCex = cex,
            });
        }

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITokenRepository>();
        await repo.UpsertBatchAsync(tokens, ct);

        _logger.LogInformation("Token sync done: {Count} tokens, {Cex} on CEX",
            tokens.Count, tokens.Count(t => t.IsAvailableOnCex));
    }

    private async Task<List<JupiterToken>> FetchJupiterTokensAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpFactory.CreateClient("jupiter");
            client.Timeout = TimeSpan.FromSeconds(3);

            var result = await client.GetFromJsonAsync<List<JupTokenV2>>(
                "https://api.jup.ag/tokens/v2/search?query=&limit=100", ct);
            if (result is { Count: > 0 })
            {
                return result.Select(t => new JupiterToken(t.symbol, t.name, t.id, t.decimals)).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch token list from Jupiter API");
        }
        return [];
    }

    private record JupTokenV2(string id, string symbol, string name, int decimals);

    private async Task<HashSet<string>> FetchBingxPairsAsync(CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("bingx");
        client.Timeout = TimeSpan.FromSeconds(3);
        try
        {
            var contracts = await client.GetFromJsonAsync<BingxContractsResponse>(
                "https://open-api-swap.bingx.com/openApi/swap/v2/quote/contracts", ct);

            if (contracts?.Data is null) return [];

            var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in contracts.Data)
            {
                var s = c.Symbol ?? "";
                if (s.EndsWith("-USDT", StringComparison.OrdinalIgnoreCase))
                    symbols.Add(s[..^5]);
            }
            return symbols;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch BingX pairs");
            return [];
        }
    }

    private record JupiterTokenList([property: JsonPropertyName("tokens")] List<JupiterToken> Tokens);
    private record JupiterToken(
        [property: JsonPropertyName("symbol")] string Symbol,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("mint")] string Mint,
        [property: JsonPropertyName("decimals")] int Decimals);

    private record BingxContractsResponse(
        [property: JsonPropertyName("data")] List<BingxContract>? Data);
    private record BingxContract(
        [property: JsonPropertyName("symbol")] string? Symbol);
}
