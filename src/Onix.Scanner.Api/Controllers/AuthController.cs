using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Api.Auth;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepo;
    private readonly ITokenRepository _tokenRepo;
    private readonly string _botUsername;
    private readonly string _appUrl;
    private readonly JwtTokenService _jwt;

    public AuthController(IUserRepository userRepo, ITokenRepository tokenRepo, IConfiguration config, JwtTokenService jwt)
    {
        _userRepo = userRepo;
        _tokenRepo = tokenRepo;
        _jwt = jwt;
        _botUsername = config.GetValue<string>("Telegram:BotUsername") ?? "YOUR_BOT";
        _appUrl = (config.GetValue<string>("App:Url") ?? "http://localhost:5000").Trim();
        if (!_appUrl.StartsWith("http://") && !_appUrl.StartsWith("https://"))
            _appUrl = "https://" + _appUrl;
    }

    private string? DeviceName =>
        Request.Headers.UserAgent.ToString();

    private string? IpAddress =>
        HttpContext.Connection.RemoteIpAddress?.ToString();

    [AllowAnonymous]
    [HttpGet("telegram")]
    public ActionResult LoginViaTelegram([FromQuery] long telegramId, [FromQuery] string? username, [FromQuery] string? name)
    {
        var botLink = $"https://t.me/{_botUsername}?start=auth_{telegramId}";
        return Ok(new { url = botLink });
    }

    [AllowAnonymous]
    [HttpPost("verify")]
    public async Task<ActionResult> VerifyTelegram([FromBody] VerifyRequest request, CancellationToken ct)
    {
        var user = await _userRepo.GetByTelegramIdAsync(request.TelegramId, ct);

        if (user is null)
        {
            user = new User
            {
                TelegramId = request.TelegramId,
                TelegramUsername = request.Username,
                DisplayName = request.DisplayName,
                LastLoginAt = DateTime.UtcNow
            };
            user = await _userRepo.CreateAsync(user, ct);
            await _tokenRepo.AddDefaultTokensAsync(user.Id, ct);
        }

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

    [Authorize]
    [HttpGet("check")]
    public ActionResult Check()
    {
        var telegramIdClaim = User.FindFirstValue("telegram_id");
        return Ok(new
        {
            userId = User.GetUserId().ToString(),
            telegramId = telegramIdClaim != null ? (long?)long.Parse(telegramIdClaim) : null,
            role = User.FindFirstValue(ClaimTypes.Role),
            tier = User.FindFirstValue("tier"),
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
    public ActionResult Me()
    {
        return Ok(new
        {
            Id = User.GetUserId(),
            TelegramId = User.GetTelegramId(),
            TelegramUsername = User.FindFirstValue("telegram_id"),
            DisplayName = User.Identity?.Name,
            role = User.FindFirstValue(ClaimTypes.Role)
        });
    }

    [AllowAnonymous]
    [HttpGet("/login/{token}")]
    public async Task<ActionResult> LoginViaMagicLink(string token, CancellationToken ct)
    {
        var loginToken = await _userRepo.ConsumeLoginTokenAsync(token, ct);
        if (loginToken is null)
            return BadRequest(new { error = "invalid_or_expired_token" });

        var user = await _userRepo.GetByIdAsync(loginToken.UserId, ct);
        if (user is null)
            return BadRequest(new { error = "user_not_found" });

        var accessToken = _jwt.GenerateAccessToken(user.Id, user.TelegramId, user.Role, user.TokenVersion, out var jti);
        var (refreshToken, hash) = _jwt.GenerateRefreshToken();

        await _userRepo.SaveRefreshTokenAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            LastJti = jti,
            DeviceName = "Telegram Magic Link",
            IpAddress = IpAddress,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        }, ct);

        return Redirect($"{_appUrl}?token={accessToken}&refresh={refreshToken}");
    }

    public class VerifyRequest
    {
        public long TelegramId { get; set; }
        public string? Username { get; set; }
        public string? DisplayName { get; set; }
    }

    public class RefreshRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class RevokeRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }
}
