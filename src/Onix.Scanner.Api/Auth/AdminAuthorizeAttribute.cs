using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared;

namespace Onix.Scanner.Api.Auth;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AdminAuthorizeAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var authToken = context.HttpContext.Request.Headers["X-Auth-Token"].FirstOrDefault();
        if (string.IsNullOrEmpty(authToken))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var userRepo = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
        var user = await userRepo.GetByAuthTokenAsync(authToken);

        if (user is null || user.Role != UserRole.Admin || user.AuthTokenExpiresAt < DateTime.UtcNow)
        {
            context.Result = new UnauthorizedResult();
            return;
        }
    }
}
