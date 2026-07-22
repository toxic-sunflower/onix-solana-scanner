using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Onix.Scanner.Core;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared;
using Onix.Scanner.Shared.Dtos;

namespace Onix.Scanner.Api.Hubs;

[Authorize]
public class SpreadHub : Hub
{
    private readonly ITokenSnapshotPool _snapshotPool;
    private readonly ITokenRepository _tokenRepo;

    public const string PremiumGroup = "premium";
    public const string FreeGroup = "free";

    public SpreadHub(ITokenSnapshotPool snapshotPool, ITokenRepository tokenRepo)
    {
        _snapshotPool = snapshotPool;
        _tokenRepo = tokenRepo;
    }

    public override async Task OnConnectedAsync()
    {
        var tier = Context.User?.FindFirstValue("tier");
        var group = tier == SubscriptionTier.Premium.ToString() ? PremiumGroup : FreeGroup;
        await Groups.AddToGroupAsync(Context.ConnectionId, group);

        var tokens = await _tokenRepo.GetAllAsync();
        foreach (var token in tokens.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.BingxSymbol)))
        {
            if (!_snapshotPool.TryGetIndex(token.Id, out var idx)) continue;
            var snap = _snapshotPool.ReadSnapshot(idx);

            var bingxPrice = snap.BingxAskPriceRaw != 0 ? snap.BingxAskPriceRaw / 1e18m : 0;
            var jupiterPrice = snap.JupiterBuyPriceRaw != 0 ? snap.JupiterBuyPriceRaw / 1e18m : 0;
            var spread = SpreadCalculator.CalculateSpread(snap.BingxAskPriceRaw, snap.JupiterBuyPriceRaw);
            var lastUpdated = snap.BingxTimestampUtc != 0 || snap.JupiterTimestampUtc != 0
                ? new DateTime(Math.Max(snap.BingxTimestampUtc, snap.JupiterTimestampUtc), DateTimeKind.Utc)
                : (DateTime?)null;

            await Clients.Caller.SendAsync("token.quote", new
            {
                version = 1,
                event_id = 0,
                token_id = token.Id,
                symbol = token.Symbol,
                bingx_ask_price = bingxPrice,
                jupiter_buy_price = jupiterPrice,
                spread_pct = spread,
                bingx_received_at = snap.BingxTimestampUtc != 0
                    ? new DateTime(snap.BingxTimestampUtc, DateTimeKind.Utc) : (DateTime?)null,
                jupiter_received_at = snap.JupiterTimestampUtc != 0
                    ? new DateTime(snap.JupiterTimestampUtc, DateTimeKind.Utc) : (DateTime?)null,
                calculated_at = DateTime.UtcNow,
                status = token.Status.ToString()
            });
        }

        await base.OnConnectedAsync();
    }
}
