using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult Get()
    {
        return Ok(new { status = "ok", timestamp = DateTime.UtcNow });
    }
}
