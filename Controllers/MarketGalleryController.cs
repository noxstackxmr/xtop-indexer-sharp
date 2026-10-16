using IndexerCore.Services.Listings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IndexerCore.Controllers;

[ApiController]
[Route("api/listings/gallery")]
[EnableRateLimiting("collections")]
public sealed class MarketGalleryController(MarketGalleryQuery gallery, PendingListingsPool pool) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string collectionId, string marketplaceId, CancellationToken cancellationToken,
        string sort = "newest", string? search = null, int page = 1, int pageSize = 24)
    {
        static bool Id(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit);
        if (!Id(collectionId) || !Id(marketplaceId) || sort is not ("newest" or "low" or "high" or "id") || search is {Length: >64} ||
            page < 1 || pageSize is < 1 or > 100 || (long)(page-1)*pageSize > int.MaxValue)
            return BadRequest(new {error="invalid_gallery_filter"});
        return Ok(await gallery.ReadAsync(Convert.FromHexString(collectionId),Convert.FromHexString(marketplaceId),sort,search ?? "",
            page,pageSize,pool.Snapshot(),cancellationToken));
    }
}
