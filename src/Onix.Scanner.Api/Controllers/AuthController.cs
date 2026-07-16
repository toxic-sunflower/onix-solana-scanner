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

    [HttpGet("telegram")]
    public ActionResult LoginViaTelegram([FromQuery] long telegramId, [FromQuery] string? username, [FromQuery] string? name)
    {
        var botLink = $"https://t.me/{_botUsername}?start=auth_{telegramId}";
        return Ok(new { url = botLink });
    }

    [HttpPost("verify")]
    public async Task<ActionResult> VerifyTelegram([FromBody] VerifyRequest request, CancellationToken ct)
    {
        var user = await _userRepo.GetByTelegramIdAsync(request.TelegramId, ct)
            ?? new User
            {
                TelegramId = request.TelegramId,
                TelegramUsername = request.Username,
                DisplayName = request.DisplayName,
                LastLoginAt = DateTime.UtcNow
            };

        if (user.Id == Guid.Empty)
            user = await _userRepo.CreateAsync(user, ct);

        var token = _jwt.GenerateToken(user.Id, user.TelegramId, user.Role);
        return Ok(new { token, userId = user.Id, expiresAt = DateTime.UtcNow.AddDays(30) });
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
}
