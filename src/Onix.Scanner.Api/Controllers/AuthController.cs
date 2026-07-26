using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Api.Auth;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Authorize]
[ExemptFromDemoQuota]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepo;
    private readonly ITokenRepository _tokenRepo;
    private readonly string _appUrl;
    private readonly JwtTokenService _jwt;
    private readonly TelegramOpenIdValidator _openIdValidator;
    private readonly TelegramOAuthClient _oauthClient;
    private readonly Services.TelegramNotificationService? _telegram;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserRepository userRepo,
        ITokenRepository tokenRepo,
        IConfiguration config,
        JwtTokenService jwt,
        TelegramOpenIdValidator openIdValidator,
        TelegramOAuthClient oauthClient,
        ILogger<AuthController> logger,
        Services.TelegramNotificationService? telegram = null)
    {
        _userRepo = userRepo;
        _tokenRepo = tokenRepo;
        _jwt = jwt;
        _openIdValidator = openIdValidator;
        _oauthClient = oauthClient;
        _telegram = telegram;
        _logger = logger;
        _appUrl = (config.GetValue<string>("App:Url") ?? "http://localhost:5000").Trim();
        if (!_appUrl.StartsWith("http://") && !_appUrl.StartsWith("https://"))
            _appUrl = "https://" + _appUrl;
    }

    private string? DeviceName =>
        Request.Headers.UserAgent.ToString();

    private string? IpAddress =>
        HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// Sole web login path: "Log In With Telegram" OAuth 2.0 + PKCE
    /// (core.telegram.org/bots/telegram-login). Frontend redirects the user
    /// to Telegram's authorization endpoint and gets back an authorization
    /// "code"; it posts that code + its PKCE code_verifier here. The backend
    /// exchanges the code for an id_token using the client secret (which
    /// never touches the browser), then validates that id_token's signature.
    /// Right after login we also proactively start the bot dialog
    /// (<see cref="Services.TelegramNotificationService.TryStartDialogAsync"/>)
    /// instead of waiting for the user to find the bot and send /start
    /// themselves — best-effort, falls back to the manual /start path.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("openid")]
    public async Task<ActionResult> LoginWithOpenId([FromBody] OpenIdLoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.CodeVerifier))
            return BadRequest(new { error = "code_and_verifier_required" });

        string? idToken;
        try
        {
            idToken = await _oauthClient.ExchangeCodeForIdTokenAsync(request.Code, request.CodeVerifier, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram OAuth code exchange threw");
            return StatusCode(500, new { error = "code_exchange_error" });
        }
        if (idToken is null)
            return Unauthorized(new { error = "code_exchange_failed" });

        ClaimsPrincipal? principal;
        try
        {
            principal = await _openIdValidator.ValidateAsync(idToken, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram id_token validation threw");
            return StatusCode(500, new { error = "id_token_validation_error" });
        }
        if (principal is null)
            return Unauthorized(new { error = "invalid_id_token" });

        var telegramId = TelegramOpenIdValidator.GetTelegramId(principal);
        if (telegramId is null)
            return Unauthorized(new { error = "invalid_id_token" });

        var username = principal.FindFirstValue("preferred_username");
        var displayName = principal.FindFirstValue("given_name") ?? principal.FindFirstValue("name") ?? username;

        try
        {
            var user = await _userRepo.GetByTelegramIdAsync(telegramId.Value, ct);
            if (user is null)
            {
                user = new User
                {
                    TelegramId = telegramId.Value,
                    TelegramUsername = username,
                    DisplayName = displayName,
                    LastLoginAt = DateTime.UtcNow
                };
                user = await _userRepo.CreateAsync(user, ct);
                await _tokenRepo.AddDefaultTokensAsync(user.Id, ct);
            }
            else
            {
                user.LastLoginAt = DateTime.UtcNow;
                await _userRepo.UpdateAsync(user, ct);
            }

            if (_telegram is not null)
                await _telegram.TryStartDialogAsync(user.Id, user.TelegramId, ct);

            var accessToken = _jwt.GenerateAccessToken(user.Id, user.TelegramId, user.Role, user.TokenVersion, out var jti);
            var (refreshToken, hash) = _jwt.GenerateRefreshToken();

            await _userRepo.SaveRefreshTokenAsync(new RefreshToken
            {
                UserId = user.Id,
                TokenHash = hash,
                DeviceName = DeviceName,
                IpAddress = IpAddress,
                LastJti = jti,
                ExpiresAt = DateTime.UtcNow.AddDays(30),
            }, ct);

            return Ok(new
            {
                token = accessToken,
                refreshToken,
                userId = user.Id,
                expiresAt = DateTime.UtcNow.AddDays(30)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create/update user or issue tokens after OpenID login");
            return StatusCode(500, new { error = "login_finalize_error" });
        }
    }

    [Authorize]
    [HttpGet("check")]
    public async Task<ActionResult> Check(CancellationToken ct)
    {
        var telegramIdClaim = User.FindFirstValue("telegram_id");
        var demo = await _userRepo.GetDemoStatusAsync(User.GetUserId(), ct);

        return Ok(new
        {
            userId = User.GetUserId().ToString(),
            telegramId = telegramIdClaim != null ? (long?)long.Parse(telegramIdClaim) : null,
            role = User.FindFirstValue(ClaimTypes.Role),
            tier = User.FindFirstValue("tier"),
            demoSecondsUsed = demo?.DemoSecondsUsed ?? 0,
            demoQuotaSeconds = DemoPolicy.QuotaSeconds,
            hasPaidAccess = demo?.HasPaidAccess ?? false,
        });
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var hash = JwtTokenService.HashRefreshToken(request.RefreshToken);
        var stored = await _userRepo.GetRefreshTokenAsync(hash, ct);
        if (stored is null)
            return Unauthorized(new { error = "invalid_refresh_token" });

        await _userRepo.DeleteRefreshTokenAsync(stored.Id, ct);

        if (stored.ExpiresAt < DateTime.UtcNow)
            return Unauthorized(new { error = "refresh_token_expired" });

        var user = await _userRepo.GetByIdAsync(stored.UserId, ct);
        if (user is null)
            return Unauthorized();

        var accessToken = _jwt.GenerateAccessToken(user.Id, user.TelegramId, user.Role, user.TokenVersion, out var jti);
        var (newRefreshToken, newHash) = _jwt.GenerateRefreshToken();

        await _userRepo.SaveRefreshTokenAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newHash,
            DeviceName = stored.DeviceName,
            IpAddress = IpAddress,
            LastJti = jti,
            LastUsedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        }, ct);

        return Ok(new
        {
            token = accessToken,
            refreshToken = newRefreshToken,
            expiresAt = DateTime.UtcNow.AddDays(30)
        });
    }

    [Authorize]
    [HttpPost("revoke")]
    public async Task<ActionResult> Revoke([FromBody] RevokeRequest request, CancellationToken ct)
    {
        var hash = JwtTokenService.HashRefreshToken(request.RefreshToken);
        var stored = await _userRepo.GetRefreshTokenAsync(hash, ct);
        if (stored is not null)
        {
            if (!string.IsNullOrEmpty(stored.LastJti))
                await _userRepo.BlacklistJtiAsync(stored.UserId, stored.LastJti, ct);
            await _userRepo.DeleteRefreshTokenAsync(stored.Id, ct);
        }

        return NoContent();
    }

    [Authorize]
    [HttpPost("revoke-all")]
    public async Task<ActionResult> RevokeAll(CancellationToken ct)
    {
        var userId = User.GetUserId();
        await _userRepo.DeleteUserRefreshTokensAsync(userId, ct);
        await _userRepo.IncrementTokenVersionAsync(userId, ct);
        return NoContent();
    }

    [Authorize]
    [HttpPost("revoke-others")]
    public async Task<ActionResult> RevokeOthers([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var hash = JwtTokenService.HashRefreshToken(request.RefreshToken);
        var current = await _userRepo.GetRefreshTokenAsync(hash, ct);
        if (current is null)
            return Unauthorized(new { error = "invalid_refresh_token" });

        var userId = User.GetUserId();
        var sessions = await _userRepo.GetSessionsAsync(userId, ct);
        var others = sessions.Where(s => s.Id != current.Id).ToList();

        if (others.Count == 0)
        {
            await _userRepo.IncrementTokenVersionAsync(userId, ct);
            var user = await _userRepo.GetByIdAsync(userId, ct);
            if (user is null) return Unauthorized();
            var accessToken = _jwt.GenerateAccessToken(user.Id, user.TelegramId, user.Role, user.TokenVersion, out var jti);
            current.LastJti = jti;
            await _userRepo.UpdateRefreshTokenLastUsedAsync(current.Id, IpAddress, ct);
            return Ok(new { token = accessToken });
        }

        var jtis = others.Select(s => s.LastJti).Where(j => !string.IsNullOrEmpty(j)).ToList();
        if (jtis.Count > 0)
            await _userRepo.BlacklistJtisAsync(userId, jtis!, ct);

        foreach (var s in others)
            await _userRepo.DeleteSessionAsync(s.Id, ct);

        var user2 = await _userRepo.GetByIdAsync(userId, ct);
        if (user2 is null) return Unauthorized();

        var accessToken2 = _jwt.GenerateAccessToken(user2.Id, user2.TelegramId, user2.Role, user2.TokenVersion, out var jti2);
        current.LastJti = jti2;
        await _userRepo.UpdateRefreshTokenLastUsedAsync(current.Id, IpAddress, ct);
        return Ok(new { token = accessToken2 });
    }

    [Authorize]
    [HttpGet("sessions")]
    public async Task<ActionResult> GetSessions([FromQuery] string? currentRefreshToken, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var sessions = await _userRepo.GetSessionsAsync(userId, ct);

        Guid? currentId = null;
        if (!string.IsNullOrEmpty(currentRefreshToken))
        {
            var hash = JwtTokenService.HashRefreshToken(currentRefreshToken);
            var current = await _userRepo.GetRefreshTokenAsync(hash, ct);
            currentId = current?.Id;
        }

        return Ok(sessions.Select(s => new
        {
            id = s.Id,
            deviceName = s.DisplayName,
            ipAddress = s.IpAddress,
            lastUsedAt = s.LastUsedAt ?? s.CreatedAt,
            createdAt = s.CreatedAt,
            expiresAt = s.ExpiresAt,
            isCurrent = s.Id == currentId
        }));
    }

    [Authorize]
    [HttpDelete("sessions/{id:guid}")]
    public async Task<ActionResult> DeleteSession(Guid id, [FromQuery] string? currentRefreshToken, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var session = await _userRepo.GetSessionByIdAsync(id, userId, ct);
        if (session is null)
            return NotFound();

        if (!string.IsNullOrEmpty(session.LastJti))
            await _userRepo.BlacklistJtiAsync(userId, session.LastJti, ct);

        await _userRepo.DeleteSessionAsync(id, ct);

        bool isCurrent = false;
        if (!string.IsNullOrEmpty(currentRefreshToken))
        {
            var hash = JwtTokenService.HashRefreshToken(currentRefreshToken);
            var current = await _userRepo.GetRefreshTokenAsync(hash, ct);
            isCurrent = current?.Id == id;
        }

        if (isCurrent)
            return NoContent();

        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult> Me(CancellationToken ct)
    {
        var user = await _userRepo.GetByIdAsync(User.GetUserId(), ct);
        if (user is null) return Unauthorized();

        return Ok(new
        {
            Id = user.Id,
            TelegramId = user.TelegramId,
            TelegramUsername = user.TelegramUsername,
            DisplayName = user.DisplayName,
            Language = user.Language,
            role = User.FindFirstValue(ClaimTypes.Role)
        });
    }

    /// <summary>Profile fields the Mini App Settings page can edit directly
    /// (TODO.md "Настройки — язык"). Everything else (spread threshold,
    /// cooldown, timezone, notifications) already lives in
    /// SettingsController — this is just the bit that's on the User row
    /// itself, not UserSettings.</summary>
    [Authorize]
    [HttpPatch("me")]
    public async Task<ActionResult> UpdateMe([FromBody] UpdateMeRequest request, CancellationToken ct)
    {
        var user = await _userRepo.GetByIdAsync(User.GetUserId(), ct);
        if (user is null) return Unauthorized();

        if (request.Language is not null)
            user.Language = request.Language;

        await _userRepo.UpdateAsync(user, ct);
        return NoContent();
    }

    /// <summary>TODO.md "Удалить аккаунт" — full account deletion, not just
    /// logout. Cleans up every table that references this user (see
    /// UserRepository.DeleteAsync); irreversible.</summary>
    [Authorize]
    [HttpDelete("me")]
    public async Task<ActionResult> DeleteMe(CancellationToken ct)
    {
        await _userRepo.DeleteAsync(User.GetUserId(), ct);
        return NoContent();
    }

    // ── Backup codes (TODO.md "Что если потерял Telegram?") ──

    private static string HashBackupCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant())));

    private static string GenerateBackupCode()
    {
        // 5 random bytes -> 10 hex chars, split for readability. Not meant to
        // be memorized — the user downloads/screenshots these once.
        var hex = Convert.ToHexString(RandomNumberGenerator.GetBytes(5));
        return $"{hex[..5]}-{hex[5..]}";
    }

    /// <summary>Regenerates the set of 10 recovery codes, invalidating any
    /// previous ones. Returns the plaintext codes exactly once — only the
    /// SHA-256 hash is persisted.</summary>
    [Authorize]
    [HttpPost("backup-codes/generate")]
    public async Task<ActionResult> GenerateBackupCodes(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var codes = Enumerable.Range(0, 10).Select(_ => GenerateBackupCode()).ToList();
        var hashes = codes.Select(HashBackupCode).ToList();
        await _userRepo.ReplaceBackupCodesAsync(userId, hashes, ct);
        _logger.LogInformation("Backup codes regenerated for user {UserId}", userId);
        return Ok(new { codes });
    }

    [Authorize]
    [HttpGet("backup-codes/count")]
    public async Task<ActionResult> BackupCodeCount(CancellationToken ct)
    {
        var count = await _userRepo.GetBackupCodeCountAsync(User.GetUserId(), ct);
        return Ok(new { count });
    }

    /// <summary>Alternate login path when the user can no longer reach
    /// Telegram — the sole other credential is a backup code generated
    /// in advance. Single-use: consumed on success.</summary>
    [AllowAnonymous]
    [HttpPost("backup-codes/login")]
    public async Task<ActionResult> LoginWithBackupCode([FromBody] BackupCodeLoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { error = "code_required" });

        var hash = HashBackupCode(request.Code);
        var stored = await _userRepo.GetByCodeHashAsync(hash, ct);
        if (stored is null)
            return Unauthorized(new { error = "invalid_code" });

        var user = await _userRepo.GetByIdAsync(stored.UserId, ct);
        if (user is null)
            return Unauthorized(new { error = "invalid_code" });

        await _userRepo.ConsumeBackupCodeAsync(stored.Id, ct);

        user.LastLoginAt = DateTime.UtcNow;
        await _userRepo.UpdateAsync(user, ct);

        var accessToken = _jwt.GenerateAccessToken(user.Id, user.TelegramId, user.Role, user.TokenVersion, out var jti);
        var (refreshToken, refreshHash) = _jwt.GenerateRefreshToken();

        await _userRepo.SaveRefreshTokenAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            DeviceName = DeviceName,
            IpAddress = IpAddress,
            LastJti = jti,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        }, ct);

        _logger.LogWarning("User {UserId} logged in via backup code (Telegram recovery path)", user.Id);

        return Ok(new
        {
            token = accessToken,
            refreshToken,
            userId = user.Id,
            expiresAt = DateTime.UtcNow.AddDays(30)
        });
    }

    public class OpenIdLoginRequest
    {
        public string Code { get; set; } = string.Empty;
        public string CodeVerifier { get; set; } = string.Empty;
    }

    public class RefreshRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class RevokeRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class UpdateMeRequest
    {
        public string? Language { get; set; }
    }

    public class BackupCodeLoginRequest
    {
        public string Code { get; set; } = string.Empty;
    }

}
