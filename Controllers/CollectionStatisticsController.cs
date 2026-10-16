using IndexerCore.Services.Collections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IndexerCore.Controllers;
[ApiController]
[Route("api/collections/{id}/statistics")]
[EnableRateLimiting("collections")]
public sealed class CollectionStatisticsController(CollectionStatisticsCache statistics) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string id, CancellationToken ct, [FromQuery] string period = "7d")
    {
        if (id.Length != 64 || !id.All(char.IsAsciiHexDigit)) return Problem(statusCode: 400, title: "invalid collection id");
        if (period is not ("24h" or "7d" or "30d" or "all")) return Problem(statusCode: 400, title: "period must be 24h, 7d, 30d or all");
        try
        {
            var result = await statistics.Get(id.ToLowerInvariant(), period, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (StatisticsBusyException)
        {
            Response.Headers.RetryAfter = "2";
            return Problem(statusCode: 503, title: "Statistics are updating. Please retry shortly.");
        }
    }
}
