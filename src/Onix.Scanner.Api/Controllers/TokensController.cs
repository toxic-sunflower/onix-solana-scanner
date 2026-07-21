using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Api.Auth;
using Onix.Scanner.Core;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Dtos;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("api/v1/tokens")]
public class TokensController : ControllerBase
{


    private readonly ITokenRepository _tokenRepo;
    private readonly ITokenSnapshotPool _snapshotPool;

    public TokensController(ITokenRepository tokenRepo, ITokenSnapshotPool snapshotPool)
    {
        _tokenRepo = tokenRepo;
        _snapshotPool = snapshotPool;
    }

    [HttpGet("debug/snapshots")]
    public ActionResult<object> DebugSnapshots()
    {
        var tokens = _tokenRepo.GetAllAsync(CancellationToken.None).Result;
        var result = new List<object>();
        foreach (var t in tokens.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.BingxSymbol)))
        {
            if (!_snapshotPool.TryGetIndex(t.Id, out var idx)) continue;
            ref var snap = ref _snapshotPool.GetSnapshot(idx);
            result.Add(new
            {
                symbol = t.Symbol,
                bingxSymbol = t.BingxSymbol,
                bingxPrice = snap.BingxAskPriceRaw / 1e18m,
                jupiterPrice = snap.JupiterBuyPriceRaw / 1e18m,
                spread = snap.BingxAskPriceRaw != 0 && snap.JupiterBuyPriceRaw != 0
                    ? (snap.BingxAskPriceRaw - snap.JupiterBuyPriceRaw) / (decimal)snap.JupiterBuyPriceRaw * 100
                    : 0,
                bingxTs = new DateTime(snap.BingxTimestampUtc, DateTimeKind.Utc),
                jupiterTs = new DateTime(snap.JupiterTimestampUtc, DateTimeKind.Utc),
                seq = snap.Sequence
            });
        }
        return Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<object>> GetAll(
        [FromQuery] string? q, [FromQuery] bool? cexOnly, [FromQuery] int offset = 0, [FromQuery] int take = 25)
    {
        var tokens = await _tokenRepo.SearchAsync(q, cexOnly);
        var popularity = await _tokenRepo.GetTokenUserCountsAsync();

        var userId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : (Guid?)null;
        HashSet<Guid>? pinnedIds = null;
        if (userId.HasValue)
        {
            pinnedIds = await _tokenRepo.GetPinnedTokenIdsAsync(userId.Value);
        }

        var all = tokens.Select(t =>
        {
            var dto = new TokenSearchDto
            {
                Id = t.Id,
                Symbol = t.Symbol,
                Name = t.Name,
                SolanaMint = t.SolanaMint,
                Decimals = t.Decimals,
                IsAvailableOnCex = t.IsAvailableOnCex,
                Popularity = popularity.GetValueOrDefault(t.Id, 0),
                IsPinned = pinnedIds?.Contains(t.Id) ?? false,
                BingxSymbol = t.BingxSymbol,
                BingxUrl = t.BingxUrl,
                JupiterUrl = t.JupiterUrl,
                SolscanUrl = t.SolscanUrl,
            };
            if (_snapshotPool.TryGetIndex(t.Id, out var idx))
            {
                var snap = _snapshotPool.ReadSnapshot(idx);
                dto.BingxAskPrice = snap.BingxAskPriceRaw != 0 ? snap.BingxAskPriceRaw / 1e18m : null;
                dto.JupiterBuyPrice = snap.JupiterBuyPriceRaw != 0 ? snap.JupiterBuyPriceRaw / 1e18m : null;
                dto.SpreadPct = CalculateSpread(snap.BingxAskPriceRaw, snap.JupiterBuyPriceRaw);
                dto.Status = t.Status;
                dto.LastUpdated = snap.BingxTimestampUtc != 0 || snap.JupiterTimestampUtc != 0
                    ? new DateTime(Math.Max(snap.BingxTimestampUtc, snap.JupiterTimestampUtc), DateTimeKind.Utc)
                    : null;
            }
            return dto;
        }).ToList();

        if (cexOnly == true)
        {
            all = all.Where(t => t.SpreadPct != null).ToList();
        }

        all.Sort((a, b) =>
        {
            var aPin = a.IsPinned ? 0 : 1;
            var bPin = b.IsPinned ? 0 : 1;
            if (aPin != bPin) return aPin - bPin;

            var aSpread = a.SpreadPct ?? 0;
            var bSpread = b.SpreadPct ?? 0;
            return bSpread.CompareTo(aSpread);
        });

        var total = all.Count;
        var items = all.Skip(offset).Take(take).ToList();
        return Ok(new { items, total });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TokenCardDto>> GetById(Guid id)
    {
        var token = await _tokenRepo.GetByIdAsync(id);
        if (token is null) return NotFound();

        if (_snapshotPool.TryGetIndex(id, out var idx))
        {
            var snap = _snapshotPool.ReadSnapshot(idx);
            return Ok(new TokenCardDto
            {
                Id = token.Id,
                Symbol = token.Symbol,
                BingxAskPrice = snap.BingxAskPriceRaw != 0 ? snap.BingxAskPriceRaw / 1e18m : 0,
                JupiterBuyPrice = snap.JupiterBuyPriceRaw != 0 ? snap.JupiterBuyPriceRaw / 1e18m : 0,
                SpreadPct = CalculateSpread(snap.BingxAskPriceRaw, snap.JupiterBuyPriceRaw),
                Status = token.Status,
                LastUpdated = snap.BingxTimestampUtc != 0 || snap.JupiterTimestampUtc != 0
                    ? new DateTime(Math.Max(snap.BingxTimestampUtc, snap.JupiterTimestampUtc), DateTimeKind.Utc)
                    : null,
                BingxUrl = token.BingxUrl,
                JupiterUrl = token.JupiterUrl,
                SolscanUrl = token.SolscanUrl
            });
        }

        return Ok(new TokenCardDto
        {
            Id = token.Id,
            Symbol = token.Symbol,
            Status = token.Status,
            BingxUrl = token.BingxUrl,
            JupiterUrl = token.JupiterUrl,
            SolscanUrl = token.SolscanUrl
        });
    }

    private static decimal CalculateSpread(long bingxRaw, long jupiterRaw)
    {
        if (bingxRaw == 0 || jupiterRaw == 0) return 0;
        var bingx = (decimal)bingxRaw / 1e18m;
        var jupiter = (decimal)jupiterRaw / 1e18m;
        if (jupiter == 0) return 0;
        return (bingx - jupiter) / jupiter * 100;
    }
}
