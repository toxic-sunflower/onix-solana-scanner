using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Api.Auth;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepo;
    private readonly string _botUsername;
    private readonly JwtTokenService _jwt;

    public AuthController(IUserRepository userRepo, IConfiguration config, JwtTokenService jwt)
    {
        _userRepo = userRepo;
        _jwt = jwt;
        _botUsername = config.GetValue<string>("Telegram:BotUsername") ?? "YOUR_BOT";
    }

    private string? DeviceName =>
        Request.Headers.UserAgent.ToString();

    private string? IpAddress =>
        HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpGet("telegram")]
    public ActionResult LoginViaTelegram([FromQuery] long telegramId, [FromQuery] string? username, [FromQuery] string? name)
    {
        var botLink = $"https://t.me/{_botUsername}?start=auth_{telegramId}";
        return Ok(new { url = botLink });
    }

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
        }

        var accessToken = _jwt.GenerateAccessToken(user.Id, user.TelegramId, user.Role, user.TokenVersion);
        var (refreshToken, hash) = _jwt.GenerateRefreshToken();

        await _userRepo.SaveRefreshTokenAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            DeviceName = DeviceName,
            IpAddress = IpAddress,
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

        var accessToken = _jwt.GenerateAccessToken(user.Id, user.TelegramId, user.Role, user.TokenVersion);
        var (newRefreshToken, newHash) = _jwt.GenerateRefreshToken();

        await _userRepo.SaveRefreshTokenAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newHash,
            DeviceName = stored.DeviceName,
            IpAddress = IpAddress,
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
            await _userRepo.DeleteRefreshTokenAsync(stored.Id, ct);

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
    [HttpGet("sessions")]
    public async Task<ActionResult> GetSessions(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var sessions = await _userRepo.GetSessionsAsync(userId, ct);
        return Ok(sessions.Select(s => new
        {
            id = s.Id,
            deviceName = s.DisplayName,
            ipAddress = s.IpAddress,
            lastUsedAt = s.LastUsedAt ?? s.CreatedAt,
            createdAt = s.CreatedAt,
            expiresAt = s.ExpiresAt,
            isCurrent = false
        }));
    }

    [Authorize]
    [HttpDelete("sessions/{id:guid}")]
    public async Task<ActionResult> DeleteSession(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var session = await _userRepo.GetSessionByIdAsync(id, userId, ct);
        if (session is null)
            return NotFound();

        await _userRepo.DeleteSessionAsync(id, ct);
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
