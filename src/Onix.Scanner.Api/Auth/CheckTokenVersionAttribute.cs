using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Onix.Scanner.Api.Auth;

namespace Onix.Scanner.Api.Auth;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class CheckTokenVersionAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var userId = context.HttpContext.User.GetUserId();
        var tokenVersion = int.Parse(context.HttpContext.User.FindFirstValue("token_version") ?? "0");

        var userRepo = context.HttpContext.RequestServices
            .GetRequiredService<Core.Contracts.IUserRepository>();

        var currentVersion = await userRepo.GetTokenVersionAsync(userId);
        if (currentVersion != tokenVersion)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "token_expired", message = "Session invalidated" });
        }
    }
}
