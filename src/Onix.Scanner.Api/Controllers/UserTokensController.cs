using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Api.Auth;
using Onix.Scanner.Core;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Dtos;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("api/v1/user-tokens")]
[Authorize]
[CheckTokenVersion]
public class UserTokensController : ControllerBase
{
    private readonly ITokenRepository _tokenRepo;
    private readonly ITokenSnapshotPool _snapshotPool;

    public UserTokensController(ITokenRepository tokenRepo, ITokenSnapshotPool snapshotPool)
    {
        _tokenRepo = tokenRepo;
        _snapshotPool = snapshotPool;
    }

    [HttpGet]
    public async Task<ActionResult<List<UserTokenDto>>> GetMyTokens(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var tokens = await _tokenRepo.GetByUserIdAsync(userId, ct);
        return Ok(tokens.Select(t =>
        {
            if (_snapshotPool.TryGetIndex(t.Id, out var idx))
            {
                var snap = _snapshotPool.ReadSnapshot(idx);
                return new UserTokenDto
                {
                    Id = t.Id,
                    Symbol = t.Symbol,
                    Name = t.Name,
                    SolanaMint = t.SolanaMint,
                    BingxSymbol = t.BingxSymbol,
                    BingxUrl = t.BingxUrl,
                    JupiterUrl = t.JupiterUrl,
                    SolscanUrl = t.SolscanUrl,
                    BingxAskPrice = snap.BingxAskPriceRaw != 0 ? snap.BingxAskPriceRaw / 1e18m : 0,
                    JupiterBuyPrice = snap.JupiterBuyPriceRaw != 0 ? snap.JupiterBuyPriceRaw / 1e18m : 0,
                    SpreadPct = SpreadCalculator.CalculateSpread(snap.BingxAskPriceRaw, snap.JupiterBuyPriceRaw),
                    LastUpdated = snap.BingxTimestampUtc != 0 || snap.JupiterTimestampUtc != 0
                        ? new DateTime(Math.Max(snap.BingxTimestampUtc, snap.JupiterTimestampUtc), DateTimeKind.Utc) : null,
                };
            }
            return new UserTokenDto
            {
                Id = t.Id,
                Symbol = t.Symbol,
                Name = t.Name,
                SolanaMint = t.SolanaMint,
                BingxSymbol = t.BingxSymbol,
                BingxUrl = t.BingxUrl,
                JupiterUrl = t.JupiterUrl,
                SolscanUrl = t.SolscanUrl,
            };
        }).ToList());
    }

    [HttpPost]
    public async Task<ActionResult> AddToken([FromBody] AddTokenRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var token = await _tokenRepo.GetByIdAsync(request.TokenId, ct);
        if (token is null)
            return BadRequest(new { error = "Token not found" });
        if (!token.IsAvailableOnCex)
            return BadRequest(new { error = "Token is not available on CEX" });

        await _tokenRepo.AddUserTokenAsync(userId, token.Id, ct);
        return Ok();
    }

    [HttpDelete("{tokenId:guid}")]
    public async Task<ActionResult> RemoveToken(Guid tokenId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        await _tokenRepo.RemoveUserTokenAsync(userId, tokenId, ct);
        return NoContent();
    }

    [HttpPatch("{tokenId:guid}/pin")]
    public async Task<ActionResult> PinToken(Guid tokenId, [FromBody] PinTokenRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        await _tokenRepo.PinTokenAsync(userId, tokenId, request.IsPinned, ct);
        return NoContent();
    }

    public record AddTokenRequest(Guid TokenId);
    public record PinTokenRequest(bool IsPinned);
}
