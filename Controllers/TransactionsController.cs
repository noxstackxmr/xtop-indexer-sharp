using IndexerCore.Models.Transactions;
using IndexerCore.Services.Transactions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IndexerCore.Controllers;

[ApiController]
[Route("api/transactions")]
[EnableRateLimiting("collections")]
public sealed class TransactionsController(TransactionStatusQueryService transactions) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ActionResult<TransactionStatusResponse>> Get(string id, CancellationToken cancellationToken)
    {
        if (id.Length != 64 || !id.All(char.IsAsciiHexDigit))
            return Problem(statusCode: 400, title: "invalid transaction id", detail: "expected 64 hex characters");
        return Ok(await transactions.GetAsync(Convert.FromHexString(id), cancellationToken));
    }
}
