using System.Data;
using System.Globalization;
using IndexerCore.Data;
using IndexerCore.Models.Collections;
using IndexerCore.Models.Items;
using IndexerCore.Models.Listings;
using IndexerCore.Monero;
using IndexerCore.Protocol.Sales;
using IndexerCore.Services.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static IndexerCore.Services.Items.ItemReadData;

namespace IndexerCore.Services.Listings;

public sealed record MarketGalleryRow(ItemResponse Item, ListingResponse? Listing, PendingListing? Pending)
{
    public string? PurchaseState { get; init; }
}
public sealed record MarketGalleryResponse(string Network, byte NetworkId, ScannedTipResponse? ScannedTip,
    ScannedTipResponse? SpendCheckedTip, int Page, int PageSize, int Total, int ConfirmedTotal, int PendingTotal,
    bool PoolAvailable, bool PoolTruncated, MarketGalleryRow[] Entries);

public sealed class MarketGalleryQuery(IndexerDbContext db, ItemQueryService items, ListingQueryService listings,
    IOptions<MoneroOptions> options)
{
    public async Task<MarketGalleryResponse> ReadAsync(byte[] collectionId, byte[] marketplaceId, string sort, string search,
        int page, int pageSize, PendingPoolSnapshot pool, CancellationToken token)
    {
        var offset = Offset(page,pageSize);
        if (collectionId.Length != 32 || marketplaceId.Length != 32 || sort is not ("newest" or "low" or "high" or "id"))
            throw new ArgumentException("invalid gallery filter");
        search = search.Trim().TrimStart('#').ToLowerInvariant();
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,token);
        var tip = await Tip(db,options.Value.XtopNetwork,false,token);
        var spend = await Tip(db,options.Value.XtopNetwork,true,token);
        var confirmed = await db.ItemTrades.AsNoTracking().Where(t =>
            t.PreviousOutput.Network == options.Value.XtopNetwork && t.PreviousOutput.Collection.ProtocolId == collectionId &&
            t.MarketplaceId == marketplaceId && (t.Operation == ListingProofs.ListOperation || t.Operation == ListingProofs.ClientListOperation) &&
            t.SuccessorOutput.Burn == null && t.SuccessorOutput.Trade == null)
            .Select(t => new { t.MessageId,ItemId=t.SuccessorOutput.ItemId!,Serial=t.SuccessorOutput.RangeStart!.Value,
                Price=t.Price!.Value,t.Message.Transaction.Block.Height,t.Message.Transaction.Position })
            .ToArrayAsync(token);
        var keys = confirmed.Select(t => new SortKey(t.MessageId,Hex(t.ItemId),t.Serial,t.Price,t.Height,t.Position,null)).ToList();
        var activeIds = confirmed.Select(c=>Hex(c.ItemId)).ToHashSet(StringComparer.Ordinal);
        var available = pool.Available && tip != null && tip == spend && pool.CheckedTip == tip.Hash;
        var pending = available ? pool.Listings.Where(p => p.PurchaseListingId == null && p.CollectionId == Hex(collectionId) && p.MarketplaceId == Hex(marketplaceId) &&
                !activeIds.Contains(p.Item.ItemId))
            .GroupBy(p=>p.Item.ItemId).Select(g=>g.OrderByDescending(p=>p.FirstSeenAt).ThenBy(p=>p.TransactionId,StringComparer.Ordinal).First()).ToArray() : [];
        keys.AddRange(pending.Select(p=>new SortKey(0,p.Item.ItemId,p.Item.Serial,decimal.Parse(p.PriceAtomic,CultureInfo.InvariantCulture),0,0,p)));
        var filtered = keys.Where(k => search.Length == 0 || k.Serial.ToString("D4",CultureInfo.InvariantCulture).Contains(search) || k.ItemId.Contains(search));
        var sorted = sort switch {
            "low" => filtered.OrderBy(k=>k.Price),
            "high" => filtered.OrderByDescending(k=>k.Price),
            "id" => filtered.OrderBy(k=>k.Serial),
            _ => filtered.OrderByDescending(k=>k.Pending != null).ThenByDescending(k=>k.Pending?.FirstSeenAt)
                .ThenByDescending(k=>k.Height).ThenByDescending(k=>k.Position).ThenByDescending(k=>k.MessageId)
        };
        var selected = sorted.ThenBy(k=>k.Serial).ThenBy(k=>k.ItemId,StringComparer.Ordinal).Skip(offset).Take(pageSize).ToArray();
        var confirmedPage = selected.Where(k=>k.Pending == null).ToArray();
        var pageListings = (await listings.ReadBatchAsync(confirmedPage.Select(k=>k.MessageId).ToArray(),spend,token)).ToDictionary(l=>l.ItemId);
        var pageItems = (await items.ReadBatchAsync(confirmedPage.Select(k=>Convert.FromHexString(k.ItemId)).ToArray(),token)).ToDictionary(i=>i.ItemId);
        var pendingPurchases = available ? pool.Listings.Where(p=>p.PurchaseListingId != null).Select(p=>p.PurchaseListingId!).ToHashSet() : [];
        var rows = selected.Select(k=>k.Pending is {} p ? new MarketGalleryRow(p.Item,null,p) :
            new MarketGalleryRow(pageItems[k.ItemId],pageListings[k.ItemId],null) {
                PurchaseState = pendingPurchases.Contains(pageListings[k.ItemId].Id) ? "purchase_pending" : null
            }).ToArray();
        var result = new MarketGalleryResponse(options.Value.Network,options.Value.XtopNetwork,tip,spend,page,pageSize,
            filtered.Count(),confirmed.Length,pending.Length,available,pool.Truncated,rows);
        await snapshot.CommitAsync(token);
        return result;
    }
    private sealed record SortKey(long MessageId,string ItemId,long Serial,decimal Price,long Height,int Position,PendingListing? Pending);
}
