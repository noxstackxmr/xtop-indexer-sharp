using IndexerCore.Services.Marketplaces;
using Microsoft.AspNetCore.Mvc;

namespace IndexerCore.Controllers;

[ApiController]
[Route("api/marketplaces/rates/xmr-usd")]
public sealed class MarketRatesController(XmrUsdRate rate) : ControllerBase
{
    [HttpGet]
    public XmrUsdQuote Get() => rate.Read();
}
