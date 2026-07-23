using System.Security.Claims;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Api.Services;
using Onix.Scanner.Core;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("api/v1/sse")]
[Authorize]
public class SseController : ControllerBase
{
    private readonly SseBroadcaster _broadcaster;
    private readonly ITokenSnapshotPool _snapshotPool;
    private readonly ITokenRepository _tokenRepo;

    public SseController(SseBroadcaster broadcaster, ITokenSnapshotPool snapshotPool, ITokenRepository tokenRepo)
    {
        _broadcaster = broadcaster;
        _snapshotPool = snapshotPool;
        _tokenRepo = tokenRepo;
    }

    [HttpGet("spread")]
    public async Task Spread(CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var tier = User.FindFirstValue("tier");
        var group = tier == SubscriptionTier.Premium.ToString() ? SseBroadcaster.PremiumGroup : SseBroadcaster.FreeGroup;

        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        var id = _broadcaster.Register(group, channel.Writer);

        try
        {
            var tokens = await _tokenRepo.GetAllAsync(ct);
            foreach (var token in tokens.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.BingxSymbol)))
            {
                if (!_snapshotPool.TryGetIndex(token.Id, out var idx)) continue;
                var snap = _snapshotPool.ReadSnapshot(idx);

                var bingxPrice = snap.BingxAskPriceRaw != 0 ? snap.BingxAskPriceRaw / 1e18m : 0;
                var jupiterPrice = snap.JupiterBuyPriceRaw != 0 ? snap.JupiterBuyPriceRaw / 1e18m : 0;
                var spread = SpreadCalculator.CalculateSpread(snap.BingxAskPriceRaw, snap.JupiterBuyPriceRaw);

                var payload = new
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
                    status = SpreadCalculator.ComputeStatus(token, snap).ToString()
                };

                await Response.WriteAsync(SseBroadcaster.Frame("token.quote", payload), ct);
            }
            await Response.Body.FlushAsync(ct);

            await foreach (var message in channel.Reader.ReadAllAsync(ct))
            {
                await Response.WriteAsync(message, ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected
        }
        finally
        {
            _broadcaster.Unregister(id);
        }
    }
}
