using Microsoft.AspNetCore.Mvc;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("api/v1/config")]
public class ConfigController : ControllerBase
{
    private readonly string _botUsername;

    public ConfigController(IConfiguration config)
    {
        _botUsername = config.GetValue<string>("Telegram:BotUsername") ?? "OnixSolanaScanner_Bot";
    }

    [HttpGet]
    public IActionResult Get() => Ok(new { botUsername = _botUsername });
}
