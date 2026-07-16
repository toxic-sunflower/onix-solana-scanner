using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Onix.Scanner.Api.Auth;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class CheckTokenVersionAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var userId = context.HttpContext.User.GetUserId();
        var tokenVersion = int.Parse(context.HttpContext.User.FindFirstValue("token_version") ?? "0");
        var jti = context.HttpContext.User.FindFirstValue(JwtRegisteredClaimNames.Jti) ?? "";

        var services = context.HttpContext.RequestServices;
        var userRepo = services.GetRequiredService<Core.Contracts.IUserRepository>();

        if (!string.IsNullOrEmpty(jti))
        {
            var blacklisted = await userRepo.IsJtiBlacklistedAsync(jti);
            if (blacklisted)
            {
                context.Result = new UnauthorizedObjectResult(new { error = "token_revoked", message = "Session terminated" });
                return;
            }
        }

        var currentVersion = await userRepo.GetTokenVersionAsync(userId);
        if (currentVersion != tokenVersion)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "token_expired", message = "Session invalidated" });
        }
    }
}
