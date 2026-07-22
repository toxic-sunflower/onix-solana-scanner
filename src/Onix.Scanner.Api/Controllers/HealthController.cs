using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting("public")]
[Route("api/v1/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult Get()
    {
        return Ok(new { status = "ok", timestamp = DateTime.UtcNow });
    }
}
