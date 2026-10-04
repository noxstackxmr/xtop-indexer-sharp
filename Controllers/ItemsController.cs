using IndexerCore.Models.Items;
using IndexerCore.Services.Items;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IndexerCore.Controllers;

[ApiController]
[Route("api/items")]
[EnableRateLimiting("collections")]
public sealed class ItemsController(ItemQueryService items) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ItemListResponse>> GetList(CancellationToken cancellationToken,
        [FromQuery] string? collectionId = null, [FromQuery] string? status = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (collectionId != null && !IsId(collectionId))
            return Problem(statusCode: 400, title: "invalid collection id", detail: "expected 64 hex characters");
        if (status is not (null or "prepared_unsold" or "sold" or "burned"))
            return Problem(statusCode: 400, title: "invalid item status", detail: "status must be prepared_unsold, sold or burned");
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return Problem(statusCode: 400, title: "invalid pagination", detail: "page must be positive, pageSize must be 1..100, and the offset must fit int32");
        return Ok(await items.ListAsync(collectionId == null ? null : Convert.FromHexString(collectionId), status, page, pageSize, cancellationToken));
    }

    [HttpGet("{itemId}")]
    public async Task<ActionResult<ItemDetailsResponse>> GetById(string itemId, CancellationToken cancellationToken)
    {
        if (!IsId(itemId)) return Problem(statusCode: 400, title: "invalid item id", detail: "expected 64 hex characters");
        var item = await items.GetAsync(Convert.FromHexString(itemId), cancellationToken);
        if (item == null) return Problem(statusCode: 404, title: "item not found");
        return Ok(item);
    }

    private static bool IsId(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit);
}
