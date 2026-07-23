using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Api.Auth;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Dtos;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("api/v1/blacklist")]
[Authorize]
public class BlacklistController : ControllerBase
{
    private readonly ITokenRepository _tokenRepo;

    public BlacklistController(ITokenRepository tokenRepo)
    {
        _tokenRepo = tokenRepo;
    }

    [HttpGet]
    public async Task<ActionResult<List<BlacklistedTokenDto>>> GetBlacklist(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var tokens = await _tokenRepo.GetBlacklistedTokensAsync(userId, ct);
        return Ok(tokens.Select(t => new BlacklistedTokenDto
        {
            Id = t.Id,
            Symbol = t.Symbol,
            Name = t.Name,
            SolanaMint = t.SolanaMint,
        }).ToList());
    }

    [HttpPost("{tokenId:guid}")]
    public async Task<ActionResult> AddToBlacklist(Guid tokenId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var token = await _tokenRepo.GetByIdAsync(tokenId, ct);
        if (token is null) return NotFound();

        await _tokenRepo.BlacklistTokenAsync(userId, tokenId, ct);
        return Ok();
    }

    [HttpDelete("{tokenId:guid}")]
    public async Task<ActionResult> RemoveFromBlacklist(Guid tokenId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        await _tokenRepo.UnblacklistTokenAsync(userId, tokenId, ct);
        return NoContent();
    }
}
