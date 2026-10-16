using System.Data;
using System.Globalization;
using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Models.Collections;
using IndexerCore.Monero;
using IndexerCore.Protocol.Sales;
using IndexerCore.Services.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IndexerCore.Services.Collections;

public sealed record CollectionStatistics(string CollectionId, string Period, DateTimeOffset FromUtc,
    DateTimeOffset UpdatedAt, ScannedTipResponse? ScannedTip, long Supply, long Minted, long Burned,
    long Owners, long Listed, string? FloorPriceAtomic, long Sales, string VolumeAtomic,
    string? MedianPriceAtomic, string? LastPriceAtomic, StatisticsPoint[] History, bool Stale = false);
public sealed record StatisticsPoint(DateTimeOffset At, string? FloorPriceAtomic, long Sales, string VolumeAtomic);

public sealed class CollectionStatisticsQuery(IndexerDbContext db, IOptions<MoneroOptions> options)
{
    public async Task<CollectionStatistics?> Read(string id, string period, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        db.Database.SetCommandTimeout(15);
        var bytes = Convert.FromHexString(id);
        var collection = await db.Collections.AsNoTracking().Where(c => c.Network == options.Value.XtopNetwork && c.ProtocolId == bytes)
            .Select(c => new { c.Id, c.MaxSupply, Created = c.CreationMessage.Transaction.Block.Timestamp }).SingleOrDefaultAsync(ct);
        if (collection is null) return null;
        var end = DateTimeOffset.UtcNow;
        var start = period switch { "24h" => end.AddDays(-1), "7d" => end.AddDays(-7), "30d" => end.AddDays(-30), _ => collection.Created < end ? collection.Created : end.AddSeconds(-1) };
        var tip = await ItemReadData.Tip(db, options.Value.XtopNetwork, true, ct);
        var outputs = db.CollectionOutputs.AsNoTracking().Where(o => o.CollectionId == collection.Id && o.Kind == CollectionOutputKind.Item);
        var owners = await outputs.Where(o => o.Trade == null && o.Burn == null && o.Purchase == null && o.Split == null &&
                (o.PurchaseOrigin != null || o.TradeOrigin != null))
            .Select(o => o.TradeOrigin != null && (o.TradeOrigin.Operation == ListingProofs.ListOperation || o.TradeOrigin.Operation == ListingProofs.ClientListOperation)
                ? o.TradeOrigin.PreviousOutput.OwnerKey : o.OwnerKey).Distinct().LongCountAsync(ct);
        var minted = await db.PrimaryPurchaseItems.LongCountAsync(i => i.CollectionId == collection.Id, ct);
        var burned = await db.ItemBurns.LongCountAsync(b => b.Output.CollectionId == collection.Id, ct);
        var active = db.ItemTrades.AsNoTracking().Where(t => t.PreviousOutput.CollectionId == collection.Id &&
            (t.Operation == ListingProofs.ListOperation || t.Operation == ListingProofs.ClientListOperation) && t.SuccessorOutput.Trade == null && t.SuccessorOutput.Burn == null);
        var listed = await active.LongCountAsync(ct);
        var floor = await active.Select(t => t.Price).MinAsync(ct);
        var cid = collection.Id;
        var rows = await db.Database.SqlQuery<StatisticsSqlRow>($"""
            WITH listings AS MATERIALIZED (
              SELECT l."Price" price, b."Timestamp" opened,
                     CASE WHEN cb."Timestamp" IS NOT NULL OR bb."Timestamp" IS NOT NULL
                          THEN GREATEST(b."Timestamp", COALESCE(cb."Timestamp", bb."Timestamp")) END closed
              FROM "ItemTrades" l
              JOIN "CollectionOutputs" o ON o."Id" = l."PreviousOutputId" AND o."CollectionId" = {cid}
              JOIN "Transactions" t ON t."Id" = l."MessageId"
              JOIN "Blocks" b ON b."Id" = t."BlockId"
              LEFT JOIN "ItemTrades" closing ON closing."PreviousOutputId" = l."SuccessorOutputId"
              LEFT JOIN "Transactions" ct ON ct."Id" = closing."MessageId"
              LEFT JOIN "Blocks" cb ON cb."Id" = ct."BlockId"
              LEFT JOIN "ItemBurns" burn ON burn."OutputId" = l."SuccessorOutputId"
              LEFT JOIN "Transactions" bt ON bt."Id" = burn."TransactionId"
              LEFT JOIN "Blocks" bb ON bb."Id" = bt."BlockId"
              WHERE l."Operation" IN ({ListingProofs.ListOperation}, {ListingProofs.ClientListOperation})
            ), sales AS MATERIALIZED (
              SELECT l."Price" price, b."Timestamp" at, b."Height" height, t."Position" position
              FROM "ItemTrades" s
              JOIN "ItemTrades" l ON l."MessageId" = s."ListingId"
              JOIN "CollectionOutputs" o ON o."Id" = s."PreviousOutputId" AND o."CollectionId" = {cid}
              JOIN "Transactions" t ON t."Id" = s."MessageId"
              JOIN "Blocks" b ON b."Id" = t."BlockId"
              WHERE s."Operation" = {SecondaryPurchaseProofs.Operation} AND b."Timestamp" >= {start} AND b."Timestamp" <= {end}
            ), totals AS (
              SELECT count(*) AS count, COALESCE(sum(price),0) AS volume,
                     percentile_disc(0.5) WITHIN GROUP (ORDER BY price) AS median FROM sales
            ), buckets AS (
              SELECT {start} + ({end} - {start}) * (i / 48.0) AS lo,
                     {start} + ({end} - {start}) * ((i+1) / 48.0) AS hi, i
              FROM generate_series(0,47) i
            )
            SELECT buckets.hi AS "At",
              (SELECT min(price) FROM listings WHERE opened <= buckets.hi AND (closed IS NULL OR closed > buckets.hi)) AS "Floor",
              (SELECT count(*) FROM sales WHERE at >= buckets.lo AND (at < buckets.hi OR (buckets.i = 47 AND at = buckets.hi))) AS "Sales",
              (SELECT COALESCE(sum(price),0) FROM sales WHERE at >= buckets.lo AND (at < buckets.hi OR (buckets.i = 47 AND at = buckets.hi))) AS "Volume",
              totals.count AS "TotalSales", totals.volume AS "TotalVolume", totals.median AS "Median",
              (SELECT price FROM sales ORDER BY height DESC, position DESC LIMIT 1) AS "Last"
            FROM buckets CROSS JOIN totals ORDER BY buckets.i
            """).ToArrayAsync(ct);
        var total = rows[0];
        await tx.CommitAsync(ct);
        return new(id, period, start, end, tip, collection.MaxSupply, minted, burned, owners, listed,
            Atomic(floor), total.TotalSales, Atomic(total.TotalVolume)!, Atomic(total.Median), Atomic(total.Last),
            rows.Select(r => new StatisticsPoint(r.At, Atomic(r.Floor), r.Sales, Atomic(r.Volume)!)).ToArray());
    }
    static string? Atomic(decimal? value) => value?.ToString("0", CultureInfo.InvariantCulture);
}

public sealed class StatisticsSqlRow
{
    public DateTimeOffset At { get; set; }
    public decimal? Floor { get; set; }
    public long Sales { get; set; }
    public decimal Volume { get; set; }
    public long TotalSales { get; set; }
    public decimal TotalVolume { get; set; }
    public decimal? Median { get; set; }
    public decimal? Last { get; set; }
}
