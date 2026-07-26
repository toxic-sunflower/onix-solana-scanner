using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared;

namespace Onix.Scanner.Api.Auth;

/// <summary>Global filter: once a non-admin, non-paying user's accumulated
/// demo seconds (DemoUsageTrackerService) cross DemoPolicy.QuotaSeconds,
/// every gated endpoint returns 402 instead of doing its normal work.
/// Skips [AllowAnonymous] endpoints and anything on a controller marked
/// [ExemptFromDemoQuota] (AuthController), so login/refresh/logout/status
/// checks keep working after the quota runs out.</summary>
public class DemoQuotaFilter : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor.EndpointMetadata.Any(m => m is AllowAnonymousAttribute))
            return;

        if (context.ActionDescriptor is ControllerActionDescriptor cad &&
            cad.ControllerTypeInfo.GetCustomAttributes(typeof(ExemptFromDemoQuotaAttribute), true).Length > 0)
            return;

        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) return;

        if (user.FindFirstValue(ClaimTypes.Role) == UserRole.Admin.ToString())
            return;

        var userId = user.GetUserId();
        var userRepo = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
        var status = await userRepo.GetDemoStatusAsync(userId, context.HttpContext.RequestAborted);
        if (status is null) return;

        if (!status.HasPaidAccess && status.DemoSecondsUsed >= DemoPolicy.QuotaSeconds)
        {
            context.Result = new ObjectResult(new
            {
                error = "demo_expired",
                message = "Demo period ended — payment required",
                demoSecondsUsed = status.DemoSecondsUsed,
                demoQuotaSeconds = DemoPolicy.QuotaSeconds,
            })
            {
                StatusCode = StatusCodes.Status402PaymentRequired
            };
        }
    }
}
