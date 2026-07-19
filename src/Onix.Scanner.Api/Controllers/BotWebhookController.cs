using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Api.Services;
using Telegram.Bot.Types;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("api/v1/bot")]
public class BotWebhookController : ControllerBase
{
    private readonly TelegramNotificationService _bot;

    public BotWebhookController(TelegramNotificationService bot)
    {
        _bot = bot;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromBody] Update update, CancellationToken ct)
    {
        await _bot.HandleUpdate(update, ct);
        return Ok();
    }
}
