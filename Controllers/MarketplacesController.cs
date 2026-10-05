using IndexerCore.Models.Marketplaces;
using IndexerCore.Services.Marketplaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IndexerCore.Controllers;

[ApiController]
[Route("api/marketplaces")]
[EnableRateLimiting("collections")]
public sealed class MarketplacesController(MarketplaceQueryService marketplaces) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MarketplaceListResponse>> GetList(CancellationToken cancellationToken,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return Problem(statusCode: 400, title: "invalid pagination", detail: "page must be positive, pageSize must be 1..100, and the offset must fit int32");
        return Ok(await marketplaces.ListAsync(page, pageSize, cancellationToken));
    }

    [HttpGet("{marketplaceId}")]
    public async Task<ActionResult<MarketplaceDetailsResponse>> GetById(string marketplaceId, CancellationToken cancellationToken,
        [FromQuery] string? configHash = null)
    {
        if (!IsId(marketplaceId) || (configHash != null && !IsId(configHash)))
            return Problem(statusCode: 400, title: "invalid marketplace id or configuration hash", detail: "expected 64 hex characters");
        var market = await marketplaces.GetAsync(Convert.FromHexString(marketplaceId),
            configHash == null ? null : Convert.FromHexString(configHash), cancellationToken);
        if (market == null) return Problem(statusCode: 404, title: "marketplace configuration not found");
        return Ok(market);
    }

    private static bool IsId(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit);
}
