using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepo;
    private readonly string _botUsername;

    public AuthController(IUserRepository userRepo, IConfiguration config)
    {
        _userRepo = userRepo;
        _botUsername = config.GetValue<string>("Telegram:BotUsername") ?? "YOUR_BOT";
    }

    [HttpGet("telegram")]
    public ActionResult LoginViaTelegram([FromQuery] long telegramId, [FromQuery] string? username, [FromQuery] string? name)
    {
        var miniAppUrl = $"{Request.Scheme}://{Request.Host}";
        var botLink = $"https://t.me/{_botUsername}?start=auth_{telegramId}";
        return Ok(new { url = botLink, miniAppUrl });
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

        var token = Guid.NewGuid().ToString("N");
        var expiresAt = DateTime.UtcNow.AddDays(30);
        await _userRepo.UpdateAuthTokenAsync(user.Id, token, expiresAt, ct);

        return Ok(new { token, userId = user.Id, expiresAt });
    }

    [HttpGet("me")]
    public async Task<ActionResult> Me([FromHeader(Name = "X-Auth-Token")] string? authToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(authToken))
            return Unauthorized();

        var user = await _userRepo.GetByAuthTokenAsync(authToken, ct);
        if (user is null)
            return Unauthorized();

        return Ok(new { user.Id, user.TelegramId, user.TelegramUsername, user.DisplayName, role = user.Role.ToString() });
    }

    public class VerifyRequest
    {
        public long TelegramId { get; set; }
        public string? Username { get; set; }
        public string? DisplayName { get; set; }
    }
}
