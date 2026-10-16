using IndexerCore.Monero;
using IndexerCore.Services.Listings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace IndexerCore.Controllers;

[ApiController]
[Route("api/listings/pending")]
[EnableRateLimiting("collections")]
public sealed class PendingListingsController(PendingListingsPool pool, IOptions<MoneroOptions> network) : ControllerBase
{
    [HttpGet]
    public IActionResult Get(string? collectionId = null, string? marketplaceId = null, int page = 1, int pageSize = 20)
    {
        static bool Id(string? value) => value == null || value.Length == 64 && value.All(char.IsAsciiHexDigit);
        if (!Id(collectionId) || !Id(marketplaceId) || page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return BadRequest(new { error = "invalid_pending_filter" });
        var snapshot = pool.Snapshot();
        var rows = snapshot.Listings.Where(l => (collectionId == null || l.CollectionId == collectionId.ToLowerInvariant()) &&
            (marketplaceId == null || l.MarketplaceId == marketplaceId.ToLowerInvariant())).ToArray();
        return Ok(new { network = network.Value.Network, networkId = network.Value.XtopNetwork, snapshot.UpdatedAt,
            snapshot.Available, snapshot.Truncated, snapshot.CheckedTip, page, pageSize, total = rows.Length, listings = rows.Skip((page - 1) * pageSize).Take(pageSize) });
    }
}
