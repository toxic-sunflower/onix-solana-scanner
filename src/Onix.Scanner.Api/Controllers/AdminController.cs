using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly ITokenRepository _tokenRepo;
    private readonly IProxyRepository _proxyRepo;

    public AdminController(ITokenRepository tokenRepo, IProxyRepository proxyRepo)
    {
        _tokenRepo = tokenRepo;
        _proxyRepo = proxyRepo;
    }

    [HttpGet("tokens")]
    public async Task<ActionResult<List<Token>>> GetAllTokens(CancellationToken ct)
    {
        return Ok(await _tokenRepo.GetAllAsync(ct));
    }

    [HttpPost("tokens")]
    public async Task<ActionResult<Token>> CreateToken([FromBody] Token token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token.SolanaMint) || token.SolanaMint.Length < 32)
            return BadRequest(new { error = "Invalid Solana Mint Address" });
        if (string.IsNullOrWhiteSpace(token.Symbol))
            return BadRequest(new { error = "Symbol is required" });
        if (string.IsNullOrWhiteSpace(token.BingxSymbol))
            return BadRequest(new { error = "BingX symbol is required" });
        if (string.IsNullOrWhiteSpace(token.JupiterInputMint))
            return BadRequest(new { error = "Jupiter input mint is required" });

        token = await _tokenRepo.CreateAsync(token, ct);
        return CreatedAtAction(nameof(GetAllTokens), new { id = token.Id }, token);
    }

    [HttpPatch("tokens/{id:guid}")]
    public async Task<ActionResult> PatchToken(Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        var existing = await _tokenRepo.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();

        foreach (var prop in body.EnumerateObject())
        {
            switch (prop.Name.ToLowerInvariant())
            {
                case "symbol": existing.Symbol = prop.Value.GetString()!; break;
                case "name": existing.Name = prop.Value.GetString(); break;
                case "solanamint": existing.SolanaMint = prop.Value.GetString()!; break;
                case "bingxsymbol": existing.BingxSymbol = prop.Value.GetString()!; break;
                case "jupiterinputmint": existing.JupiterInputMint = prop.Value.GetString()!; break;
                case "quoteamount": existing.QuoteAmount = prop.Value.GetDecimal(); break;
                case "bingxurl": existing.BingxUrl = prop.Value.GetString()!; break;
                case "jupiterurl": existing.JupiterUrl = prop.Value.GetString()!; break;
                case "solscanurl": existing.SolscanUrl = prop.Value.GetString()!; break;
                case "enabled": existing.Enabled = prop.Value.GetBoolean(); break;
                case "telegramenabled": existing.TelegramEnabled = prop.Value.GetBoolean(); break;
            }
        }

        existing.UpdatedAt = DateTime.UtcNow;
        await _tokenRepo.UpdateAsync(existing, ct);
        return NoContent();
    }

    [HttpGet("proxies")]
    public async Task<ActionResult<List<Proxy>>> GetAllProxies(CancellationToken ct)
    {
        return Ok(await _proxyRepo.GetAllAsync(ct));
    }

    [HttpPost("proxies")]
    public async Task<ActionResult<Proxy>> CreateProxy([FromBody] Proxy proxy, CancellationToken ct)
    {
        proxy.Id = Guid.NewGuid();
        proxy.CreatedAt = DateTime.UtcNow;
        proxy.UpdatedAt = DateTime.UtcNow;
        await _proxyRepo.CreateAsync(proxy, ct);
        return CreatedAtAction(nameof(GetAllProxies), new { id = proxy.Id }, proxy);
    }

    [HttpPost("proxies/{id:guid}/test")]
    public async Task<ActionResult> TestProxy(Guid id, CancellationToken ct)
    {
        var proxy = await _proxyRepo.GetByIdAsync(id, ct);
        if (proxy is null) return NotFound();
        return Ok(new { status = "test_not_implemented" });
    }

    [HttpGet("health")]
    public ActionResult Health()
    {
        return Ok(new { status = "ok", timestamp = DateTime.UtcNow });
    }
}
