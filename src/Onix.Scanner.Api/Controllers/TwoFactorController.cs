using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Api.Auth;
using Onix.Scanner.Core.Contracts;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("api/v1/2fa")]
[Authorize]
public class TwoFactorController : ControllerBase
{
    private readonly TotpService _totp;
    private readonly IUserRepository _userRepo;

    public TwoFactorController(TotpService totp, IUserRepository userRepo)
    {
        _totp = totp;
        _userRepo = userRepo;
    }

    [HttpGet("setup")]
    public async Task<ActionResult> Setup(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await _userRepo.GetByIdAsync(userId, ct);
        if (user is null) return Unauthorized();

        var secret = _totp.GenerateSecret();
        var qrUri = _totp.GenerateQrUri(secret, user.DisplayName ?? user.TelegramId.ToString());

        var backupCodes = _totp.GenerateBackupCodes(8);
        var plainCodes = backupCodes.Select(c => c.plain).ToList();
        var hashedCodes = string.Join(",", backupCodes.Select(c => c.hash));

        return Ok(new
        {
            secret,
            qrUri,
            backupCodes = plainCodes,
        });
    }

    [HttpPost("enable")]
    public async Task<ActionResult> Enable([FromBody] Enable2FARequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await _userRepo.GetByIdAsync(userId, ct);
        if (user is null) return Unauthorized();

        if (!_totp.ValidateCode(request.Secret, request.Code))
            return BadRequest(new { error = "invalid_code" });

        var backupCodes = _totp.GenerateBackupCodes(8);
        var hashedCodes = string.Join(",", backupCodes.Select(c => c.hash));

        user.Is2FAEnabled = true;
        user.TwoFactorSecret = request.Secret;
        user.TwoFactorBackupCodes = hashedCodes;

        await _userRepo.UpdateAsync(user, ct);

        return Ok(new { backupCodes = backupCodes.Select(c => c.plain).ToList() });
    }

    [HttpPost("disable")]
    public async Task<ActionResult> Disable([FromBody] Disable2FARequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await _userRepo.GetByIdAsync(userId, ct);
        if (user is null || !user.Is2FAEnabled) return BadRequest();

        if (!_totp.ValidateCode(user.TwoFactorSecret!, request.Code))
            return BadRequest(new { error = "invalid_code" });

        user.Is2FAEnabled = false;
        user.TwoFactorSecret = null;
        user.TwoFactorBackupCodes = null;
        await _userRepo.UpdateAsync(user, ct);

        return NoContent();
    }

    [HttpPost("validate")]
    public async Task<ActionResult> Validate([FromBody] Validate2FARequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await _userRepo.GetByIdAsync(userId, ct);
        if (user is null || !user.Is2FAEnabled) return BadRequest();

        if (_totp.ValidateCode(user.TwoFactorSecret!, request.Code))
            return Ok(new { valid = true });

        if (user.TwoFactorBackupCodes is not null)
        {
            var (valid, matchedHash) = _totp.ValidateBackupCode(request.Code, user.TwoFactorBackupCodes);
            if (valid && matchedHash is not null)
            {
                user.TwoFactorBackupCodes = _totp.RemoveUsedBackupCode(user.TwoFactorBackupCodes, matchedHash);
                await _userRepo.UpdateAsync(user, ct);
                return Ok(new { valid = true, usedBackup = true });
            }
        }

        return Ok(new { valid = false });
    }

    public class Enable2FARequest
    {
        public string Secret { get; set; } = "";
        public string Code { get; set; } = "";
    }

    public class Disable2FARequest
    {
        public string Code { get; set; } = "";
    }

    public class Validate2FARequest
    {
        public string Code { get; set; } = "";
    }
}
