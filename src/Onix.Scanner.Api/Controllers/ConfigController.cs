using Microsoft.AspNetCore.Mvc;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("api/v1/config")]
public class ConfigController : ControllerBase
{
    private readonly string _botUsername;
    private readonly string _oauthClientId;
    private readonly string _oauthAuthorizationEndpoint;
    private readonly string _oauthRedirectUri;

    public ConfigController(IConfiguration config)
    {
        _botUsername = config.GetValue<string>("Telegram:BotUsername") ?? "OnixSolanaScanner_Bot";
        // client_id and redirect_uri are not secrets in OAuth — only client_secret is.
        _oauthClientId = config.GetValue<string>("Telegram:OpenId:ClientId") ?? "";
        _oauthAuthorizationEndpoint = config.GetValue<string>("Telegram:OpenId:AuthorizationEndpoint")
            ?? "https://oauth.telegram.org/auth";
        _oauthRedirectUri = config.GetValue<string>("Telegram:OpenId:RedirectUri") ?? "";
    }

    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        botUsername = _botUsername,
        oauthClientId = _oauthClientId,
        oauthAuthorizationEndpoint = _oauthAuthorizationEndpoint,
        oauthRedirectUri = _oauthRedirectUri,
    });
}
