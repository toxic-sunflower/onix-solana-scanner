using Microsoft.AspNetCore.Mvc;
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

    [HttpGet("search")]
    public async Task<ActionResult<object>> Search(
        [FromQuery] string? q, [FromQuery] bool? cexOnly, [FromQuery] int offset = 0, [FromQuery] int take = 25)
    {
        var tokens = await _tokenRepo.SearchAsync(q, cexOnly);
        var popularity = await _tokenRepo.GetTokenUserCountsAsync();

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

        all.Sort((a, b) =>
        {
            var aHasSpread = a.IsAvailableOnCex && a.SpreadPct is > 0 ? 0 : 1;
            var bHasSpread = b.IsAvailableOnCex && b.SpreadPct is > 0 ? 0 : 1;
            if (aHasSpread != bHasSpread) return aHasSpread - bHasSpread;

            var aCex = a.IsAvailableOnCex ? 0 : 1;
            var bCex = b.IsAvailableOnCex ? 0 : 1;
            if (aCex != bCex) return aCex - bCex;

            var cmp = b.Popularity.CompareTo(a.Popularity);
            if (cmp != 0) return cmp;

            return string.Compare(a.Symbol, b.Symbol, StringComparison.OrdinalIgnoreCase);
        });

        var total = all.Count;
        var items = all.Skip(offset).Take(take).ToList();
        return Ok(new { items, total });
    }

    [HttpGet]
    public async Task<ActionResult<List<TokenCardDto>>> GetAll()
    {
        var tokens = await _tokenRepo.GetAllAsync();
        var result = tokens.Select(t =>
        {
            if (_snapshotPool.TryGetIndex(t.Id, out var idx))
            {
                var snap = _snapshotPool.ReadSnapshot(idx);
                return new TokenCardDto
                {
                    Id = t.Id,
                    Symbol = t.Symbol,
                    BingxAskPrice = snap.BingxAskPriceRaw != 0 ? snap.BingxAskPriceRaw / 1e18m : 0,
                    JupiterBuyPrice = snap.JupiterBuyPriceRaw != 0 ? snap.JupiterBuyPriceRaw / 1e18m : 0,
                    SpreadPct = CalculateSpread(snap.BingxAskPriceRaw, snap.JupiterBuyPriceRaw),
                    Status = t.Status,
                    LastUpdated = snap.BingxTimestampUtc != 0 || snap.JupiterTimestampUtc != 0
                        ? new DateTime(Math.Max(snap.BingxTimestampUtc, snap.JupiterTimestampUtc), DateTimeKind.Utc)
                        : null,
                    BingxUrl = t.BingxUrl,
                    JupiterUrl = t.JupiterUrl,
                    SolscanUrl = t.SolscanUrl
                };
            }

            return new TokenCardDto
            {
                Id = t.Id,
                Symbol = t.Symbol,
                Status = t.Status,
                BingxUrl = t.BingxUrl,
                JupiterUrl = t.JupiterUrl,
                SolscanUrl = t.SolscanUrl
            };
        }).ToList();

        return Ok(result);
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
