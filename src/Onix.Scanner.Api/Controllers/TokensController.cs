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

    [HttpGet]
    public async Task<ActionResult<List<TokenCardDto>>> GetAll()
    {
        var tokens = await _tokenRepo.GetAllAsync();
        var result = tokens.Select(t =>
        {
            if (_snapshotPool.TryGetIndex(t.Id, out var idx))
            {
                ref var snap = ref _snapshotPool.GetSnapshot(idx);
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
            ref var snap = ref _snapshotPool.GetSnapshot(idx);
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
