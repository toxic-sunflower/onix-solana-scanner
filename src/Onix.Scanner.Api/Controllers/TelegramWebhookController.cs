using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Api.Services;
using Telegram.Bot.Types;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/telegram/webhook")]
public class TelegramWebhookController : ControllerBase
{
    private readonly TelegramNotificationService _telegram;
    private readonly ILogger<TelegramWebhookController> _logger;

    public TelegramWebhookController(TelegramNotificationService telegram, ILogger<TelegramWebhookController> logger)
    {
        _telegram = telegram;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult> Receive([FromBody] Update update, CancellationToken ct)
    {
        // Telegram echoes back the secret we set via SetWebhookAsync in this
        // header on every request — the only thing standing between this
        // public endpoint and anyone on the internet posting fake updates.
        var secret = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
        var expected = _telegram.WebhookSecret;
        if (string.IsNullOrEmpty(expected) || secret != expected)
        {
            _logger.LogWarning("Rejected Telegram webhook request with invalid secret token");
            return Unauthorized();
        }

        await _telegram.HandleWebhookUpdateAsync(update, ct);
        return Ok();
    }
}
