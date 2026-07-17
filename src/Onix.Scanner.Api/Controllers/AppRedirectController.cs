using Microsoft.AspNetCore.Mvc;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("app")]
public class AppRedirectController : ControllerBase
{
    [HttpGet]
    public ContentResult Index([FromQuery] string name)
    {
        var html = $@"<!DOCTYPE html>
<html>
<head><meta charset=""utf-8""><title>Redirecting...</title></head>
<body><script>
(function() {{
    var apps = {{
        google: {{
            ios: 'https://apps.apple.com/app/google-authenticator/id599085139',
            android: 'https://play.google.com/store/apps/details?id=com.google.android.apps.authenticator2',
            desktop: 'https://support.google.com/accounts/answer/1066447'
        }},
        authy: {{
            ios: 'https://apps.apple.com/app/authy/id494168017',
            android: 'https://play.google.com/store/apps/details?id=com.authy.authy',
            desktop: 'https://authy.com'
        }},
        microsoft: {{
            ios: 'https://apps.apple.com/app/microsoft-authenticator/id983156458',
            android: 'https://play.google.com/store/apps/details?id=com.microsoft.authenticator',
            desktop: 'https://www.microsoft.com/en-us/security/mobile-authenticator-app'
        }},
        '2fas': {{
            ios: 'https://apps.apple.com/app/2fas-auth/id1217793794',
            android: 'https://play.google.com/store/apps/details?id=com.authenticator2.android',
            desktop: 'https://2fas.com'
        }}
    }};
    var name = '{name}';
    var ua = navigator.userAgent;
    var target = apps[name];
    if (!target) {{ document.body.innerText = 'Unknown app'; return; }}
    var url;
    if (/iPhone|iPad|iPod/.test(ua)) url = target.ios;
    else if (/Android/.test(ua)) url = target.android;
    else url = target.desktop;
    location.replace(url);
}})();
</script></body>
</html>";
        return new ContentResult
        {
            Content = html,
            ContentType = "text/html",
            StatusCode = 200
        };
    }
}
