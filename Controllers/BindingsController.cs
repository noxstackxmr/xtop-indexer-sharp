using System.Data;
using IndexerCore.Data;
using IndexerCore.Monero;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static IndexerCore.Services.Items.ItemReadData;

namespace IndexerCore.Controllers;

[ApiController]
[Route("api/bindings")]
[EnableRateLimiting("collections")]
public sealed class BindingsController(IndexerDbContext db, IOptions<MoneroOptions> options) : ControllerBase
{
    [HttpGet("{keyImage}")]
    public async Task<IActionResult> Get(string keyImage, CancellationToken cancellationToken)
    {
        if (keyImage.Length != 64 || !keyImage.All(char.IsAsciiHexDigit))
            return Problem(statusCode: 400, title: "invalid key image");
        var bytes = Convert.FromHexString(keyImage);
        var network = options.Value.XtopNetwork;
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var scanned = await Tip(db, network, false, cancellationToken);
        var processed = await Tip(db, network, true, cancellationToken);
        var bound = await db.CollectionOutputs.AnyAsync(o => o.Network == network && o.KeyImage == bytes, cancellationToken) ||
            await db.CollectionChanges.AnyAsync(o => o.Network == network && o.KeyImage == bytes, cancellationToken);
        await snapshot.CommitAsync(cancellationToken);
        return Ok(new { network = options.Value.Network, networkId = network, scannedTip = scanned, spendCheckedTip = processed, keyImage, bound });
    }
}
