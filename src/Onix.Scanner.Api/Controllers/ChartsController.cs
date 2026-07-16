using Microsoft.AspNetCore.Mvc;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Shared.Dtos;

namespace Onix.Scanner.Api.Controllers;

[ApiController]
[Route("api/v1/tokens/{tokenId:guid}/chart")]
public class ChartsController : ControllerBase
{
    private readonly ISpreadTickRepository _spreadRepo;

    public ChartsController(ISpreadTickRepository spreadRepo)
    {
        _spreadRepo = spreadRepo;
    }

    [HttpGet]
    public async Task<ActionResult<ChartResponseDto>> GetChart(
        Guid tokenId,
        [FromQuery] string interval = "5m",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string timezone = "UTC",
        CancellationToken ct = default)
    {
        var toDate = to ?? DateTime.UtcNow;
        var fromDate = from ?? toDate.AddHours(-72);

        var chart = await _spreadRepo.GetChartAsync(tokenId, interval, fromDate, toDate, timezone, ct);
        return Ok(chart);
    }
}
