using Microsoft.AspNetCore.Mvc;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("app")]
public class AppRedirectController : ControllerBase
{
    private static readonly Dictionary<string, (string ios, string android, string desktop)> Apps = new()
    {
        ["google"] = (
            "https://apps.apple.com/app/google-authenticator/id599085139",
            "https://play.google.com/store/apps/details?id=com.google.android.apps.authenticator2",
            "https://support.google.com/accounts/answer/1066447"
        ),
        ["authy"] = (
            "https://apps.apple.com/app/authy/id494168017",
            "https://play.google.com/store/apps/details?id=com.authy.authy",
            "https://authy.com"
        ),
        ["microsoft"] = (
            "https://apps.apple.com/app/microsoft-authenticator/id983156458",
            "https://play.google.com/store/apps/details?id=com.microsoft.authenticator",
            "https://www.microsoft.com/en-us/security/mobile-authenticator-app"
        ),
        ["2fas"] = (
            "https://apps.apple.com/app/2fas-auth/id1217793794",
            "https://play.google.com/store/apps/details?id=com.authenticator2.android",
            "https://2fas.com"
        ),
    };

    [HttpGet("{name}")]
    public IActionResult RedirectToApp(string name)
    {
        if (!Apps.TryGetValue(name.ToLowerInvariant(), out var urls))
            return NotFound();

        var ua = Request.Headers.UserAgent.ToString();

        if (ua.Contains("iPhone") || ua.Contains("iPad") || ua.Contains("iPod"))
            return Redirect(urls.ios);
        if (ua.Contains("Android"))
            return Redirect(urls.android);

        return Redirect(urls.desktop);
    }
}
