using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Api.Auth;
using Onix.Scanner.Core;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Admin")]
[CheckTokenVersion]
public class AdminController : ControllerBase
{
    private readonly ITokenRepository _tokenRepo;
    private readonly IProxyRepository _proxyRepo;
    private readonly ILogger<AdminController> _logger;

    public AdminController(ITokenRepository tokenRepo, IProxyRepository proxyRepo, ILogger<AdminController> logger)
    {
        _tokenRepo = tokenRepo;
        _proxyRepo = proxyRepo;
        _logger = logger;
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

        // A manually-created token is, by definition, an admin-confirmed mapping.
        token.RequiresMapping = false;

        token = await _tokenRepo.CreateAsync(token, ct);
        _logger.LogInformation("Admin created token {Symbol} ({Mint}) manually", token.Symbol, token.SolanaMint);
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
                case "solanamint":
                    // TZ п.5: "Изменение Mint Address должно логироваться."
                    var newMint = prop.Value.GetString()!;
                    if (!string.Equals(existing.SolanaMint, newMint, StringComparison.Ordinal))
                    {
                        _logger.LogWarning(
                            "Admin changed Mint Address for token {TokenId} ({Symbol}): {OldMint} -> {NewMint}",
                            existing.Id, existing.Symbol, existing.SolanaMint, newMint);
                        existing.SolanaMint = newMint;
                    }
                    break;
                case "bingxsymbol": existing.BingxSymbol = prop.Value.GetString()!; break;
                case "jupiterinputmint": existing.JupiterInputMint = prop.Value.GetString()!; break;
                case "jupiterinputdecimals": existing.JupiterInputDecimals = prop.Value.GetInt32(); break;
                case "quoteamount":
                    await _tokenRepo.SetQuoteAmountAsync(id, prop.Value.GetDecimal(), ct);
                    break;
                case "bingxurl": existing.BingxUrl = prop.Value.GetString()!; break;
                case "jupiterurl": existing.JupiterUrl = prop.Value.GetString()!; break;
                case "solscanurl": existing.SolscanUrl = prop.Value.GetString()!; break;
                case "enabled": existing.Enabled = prop.Value.GetBoolean(); break;
                case "telegramenabled": existing.TelegramEnabled = prop.Value.GetBoolean(); break;
                case "proxyid": existing.ProxyId = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetGuid(); break;
                case "proxyfallbackpolicy":
                    if (Enum.TryParse<Onix.Scanner.Shared.ProxyFallbackPolicy>(prop.Value.GetString(), true, out var policy))
                        existing.ProxyFallbackPolicy = policy;
                    break;
            }
        }

        existing.UpdatedAt = DateTime.UtcNow;
        await _tokenRepo.UpdateAsync(existing, ct);
        return NoContent();
    }

    [HttpDelete("tokens/{id:guid}")]
    public async Task<ActionResult> DeleteToken(Guid id, CancellationToken ct)
    {
        var existing = await _tokenRepo.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();
        await _tokenRepo.DeleteAsync(id, ct);
        _logger.LogInformation("Admin deleted token {Symbol} ({Mint})", existing.Symbol, existing.SolanaMint);
        return NoContent();
    }

    /// <summary>TZ п.5: admin confirms a Mapping Required token really is the
    /// right project — clears the gate and turns monitoring on.</summary>
    [HttpPost("tokens/{id:guid}/confirm-mapping")]
    public async Task<ActionResult> ConfirmMapping(Guid id, CancellationToken ct)
    {
        var existing = await _tokenRepo.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();

        existing.RequiresMapping = false;
        existing.Enabled = true;
        existing.UpdatedAt = DateTime.UtcNow;
        await _tokenRepo.UpdateAsync(existing, ct);
        _logger.LogInformation("Admin confirmed mapping for token {Symbol} ({Mint})", existing.Symbol, existing.SolanaMint);
        return NoContent();
    }

    /// <summary>Admin looked at a Mapping Required candidate and decided it's
    /// not the right project (or just doesn't want it) — clears the gate
    /// without enabling it. TokenSyncService won't touch Enabled/
    /// RequiresMapping again for this row once it exists.</summary>
    [HttpPost("tokens/{id:guid}/reject-mapping")]
    public async Task<ActionResult> RejectMapping(Guid id, CancellationToken ct)
    {
        var existing = await _tokenRepo.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();

        existing.RequiresMapping = false;
        existing.Enabled = false;
        existing.UpdatedAt = DateTime.UtcNow;
        await _tokenRepo.UpdateAsync(existing, ct);
        _logger.LogInformation("Admin rejected mapping for token {Symbol} ({Mint})", existing.Symbol, existing.SolanaMint);
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

    [HttpDelete("proxies/{id:guid}")]
    public async Task<ActionResult> DeleteProxy(Guid id, CancellationToken ct)
    {
        var existing = await _proxyRepo.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();
        await _proxyRepo.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("proxies/{id:guid}/test")]
    public async Task<ActionResult> TestProxy(Guid id, CancellationToken ct)
    {
        var proxy = await _proxyRepo.GetByIdAsync(id, ct);
        if (proxy is null) return NotFound();

        var result = await ProxyTester.TestAsync(proxy, ct);

        proxy.Status = result.Success ? Onix.Scanner.Shared.ProxyStatus.Active : Onix.Scanner.Shared.ProxyStatus.Failed;
        proxy.LatencyMs = result.LatencyMs;
        proxy.LastCheckAt = DateTime.UtcNow;
        await _proxyRepo.UpdateAsync(proxy, ct);

        return Ok(new { status = proxy.Status.ToString(), latencyMs = result.LatencyMs, error = result.Error });
    }
}
