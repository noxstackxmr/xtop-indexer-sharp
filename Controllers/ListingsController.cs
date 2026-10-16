using IndexerCore.Models.Listings;
using IndexerCore.Services.Listings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IndexerCore.Controllers;

[ApiController]
[Route("api/listings")]
[EnableRateLimiting("collections")]
public sealed class ListingsController(ListingQueryService listings) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ListingListResponse>> GetList(CancellationToken cancellationToken,
        [FromQuery] string? collectionId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? sellerOwnerKey = null, [FromQuery] string? marketplaceId = null, [FromQuery] string? mode = null)
    {
        if (collectionId != null && !IsId(collectionId))
            return Problem(statusCode: 400, title: "invalid collection id", detail: "expected 64 hex characters");
        if (sellerOwnerKey != null && !IsId(sellerOwnerKey) || marketplaceId != null && !IsId(marketplaceId) || mode != null && mode is not ("client" or "marketplace"))
            return Problem(statusCode: 400, title: "invalid listing filter");
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return Problem(statusCode: 400, title: "invalid pagination", detail: "page must be positive, pageSize must be 1..100, and the offset must fit int32");
        return Ok(await listings.ListAsync(collectionId == null ? null : Convert.FromHexString(collectionId), page, pageSize, cancellationToken,
            sellerOwnerKey == null ? null : Convert.FromHexString(sellerOwnerKey), marketplaceId == null ? null : Convert.FromHexString(marketplaceId), mode));
    }

    [HttpGet("{listingId}")]
    public async Task<ActionResult<ListingDetailsResponse>> GetById(string listingId, CancellationToken cancellationToken)
    {
        if (!IsId(listingId)) return Problem(statusCode: 400, title: "invalid listing id", detail: "expected 64 hex characters");
        var listing = await listings.GetAsync(Convert.FromHexString(listingId), cancellationToken);
        if (listing == null) return Problem(statusCode: 404, title: "listing not found");
        return Ok(listing);
    }

    private static bool IsId(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit);
}
