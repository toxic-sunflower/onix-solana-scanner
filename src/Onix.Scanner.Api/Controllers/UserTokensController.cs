using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Dtos;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("api/v1/user-tokens")]
public class UserTokensController : ControllerBase
{
    private readonly ITokenRepository _tokenRepo;
    private readonly IUserRepository _userRepo;

    public UserTokensController(ITokenRepository tokenRepo, IUserRepository userRepo)
    {
        _tokenRepo = tokenRepo;
        _userRepo = userRepo;
    }

    [HttpGet]
    public async Task<ActionResult<List<UserTokenDto>>> GetMyTokens(
        [FromHeader(Name = "X-Auth-Token")] string? authToken, CancellationToken ct)
    {
        var user = await ResolveUser(authToken, ct);
        if (user is null) return Unauthorized();

        var tokens = await _tokenRepo.GetByUserIdAsync(user.Id, ct);
        return Ok(tokens.Select(t => Map(t, user)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult> AddToken(
        [FromHeader(Name = "X-Auth-Token")] string? authToken,
        [FromBody] AddTokenRequest request, CancellationToken ct)
    {
        var user = await ResolveUser(authToken, ct);
        if (user is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.SolanaMint) || request.SolanaMint.Length < 32)
            return BadRequest(new { error = "Invalid Solana Mint Address" });
        if (string.IsNullOrWhiteSpace(request.Symbol))
            return BadRequest(new { error = "Symbol is required" });

        var existingToken = await _tokenRepo.GetByMintAsync(request.SolanaMint, ct);
        if (existingToken is null)
        {
            existingToken = new Token
            {
                Symbol = request.Symbol,
                Name = request.Name,
                SolanaMint = request.SolanaMint,
                BingxSymbol = request.BingxSymbol ?? $"{request.Symbol}-USDT",
                JupiterInputMint = "So11111111111111111111111111111111111111112",
                QuoteAmount = request.QuoteAmount,
                BingxUrl = $"https://www.bingx.com/en-us/futures/{request.Symbol}-USDT",
                JupiterUrl = $"https://jup.ag/swap/SOL-{request.Symbol}",
                SolscanUrl = $"https://solscan.io/token/{request.SolanaMint}",
                Enabled = true,
                TelegramEnabled = true
            };
            existingToken = await _tokenRepo.CreateAsync(existingToken, ct);
        }

        await _tokenRepo.AddUserTokenAsync(user.Id, existingToken.Id, ct);
        return Ok(Map(existingToken, user));
    }

    [HttpDelete("{tokenId:guid}")]
    public async Task<ActionResult> RemoveToken(
        [FromHeader(Name = "X-Auth-Token")] string? authToken,
        Guid tokenId, CancellationToken ct)
    {
        var user = await ResolveUser(authToken, ct);
        if (user is null) return Unauthorized();

        await _tokenRepo.RemoveUserTokenAsync(user.Id, tokenId, ct);
        return NoContent();
    }

    private async Task<User?> ResolveUser(string? authToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(authToken)) return null;
        return await _userRepo.GetByAuthTokenAsync(authToken, ct);
    }

    private static UserTokenDto Map(Token t, User user) => new()
    {
        Id = t.Id,
        Symbol = t.Symbol,
        Name = t.Name,
        SolanaMint = t.SolanaMint,
        BingxSymbol = t.BingxSymbol,
        BingxUrl = t.BingxUrl,
        JupiterUrl = t.JupiterUrl,
        SolscanUrl = t.SolscanUrl
    };

    public class AddTokenRequest
    {
        public string SolanaMint { get; set; } = string.Empty;
        public string? Symbol { get; set; }
        public string? Name { get; set; }
        public string? BingxSymbol { get; set; }
        public decimal QuoteAmount { get; set; } = 0.01m;
    }
}
