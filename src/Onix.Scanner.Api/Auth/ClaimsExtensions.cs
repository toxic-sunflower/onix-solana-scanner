using System.Security.Claims;

namespace Onix.Scanner.Api.Auth;

public static class ClaimsExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var val = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.Parse(val!);
    }

    public static long GetTelegramId(this ClaimsPrincipal user)
    {
        var val = user.FindFirstValue("telegram_id");
        return long.Parse(val!);
    }
}
