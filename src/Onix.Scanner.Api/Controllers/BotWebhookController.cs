using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Api.Services;
using Telegram.Bot.Types;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("api/v1/bot")]
public class BotWebhookController : ControllerBase
{
    private readonly TelegramNotificationService _bot;
    private readonly string _secret;

    public BotWebhookController(TelegramNotificationService bot, IConfiguration config)
    {
        _bot = bot;
        _secret = config["Telegram:WebhookSecret"] ?? "";
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromBody] Update update, CancellationToken ct)
    {
        var headerSecret = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();
        if (string.IsNullOrEmpty(_secret) || headerSecret != _secret)
            return Unauthorized();

        await _bot.HandleUpdate(update, ct);
        return Ok();
    }
}
