using IndexerCore.Models.Collections;
using IndexerCore.Services.Collections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IndexerCore.Controllers;

[ApiController]
[Route("api/collections")]
[EnableRateLimiting("collections")]
public sealed class CollectionsController(CollectionQueryService collections) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CollectionListResponse>> GetList(CancellationToken cancellationToken,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return Problem(statusCode: 400, title: "invalid pagination",
                detail: "page must be positive, pageSize must be 1..100, and the offset must fit int32");
        return Ok(await collections.ListAsync(page, pageSize, cancellationToken));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CollectionDetailsResponse>> GetById(string id, CancellationToken cancellationToken)
    {
        if (id.Length != 64 || !id.All(char.IsAsciiHexDigit))
            return Problem(statusCode: 400, title: "invalid collection id", detail: "expected a 64-character hex transaction id");
        var collection = await collections.GetAsync(Convert.FromHexString(id), cancellationToken);
        if (collection == null) return Problem(statusCode: 404, title: "collection not found");
        return Ok(collection);
    }
}
