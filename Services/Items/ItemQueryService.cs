using System.Data;
using System.Globalization;
using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Models.Collections;
using IndexerCore.Models.Items;
using IndexerCore.Monero;
using IndexerCore.Services.Collections;
using IndexerCore.Protocol.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IndexerCore.Services.Items;

public sealed class ItemQueryService(IndexerDbContext db, CollectionStateService states, IOptions<MoneroOptions> options)
{
    public async Task<ItemListResponse> ListAsync(byte[]? collectionId, string? status, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 100);
        if (collectionId is { Length: not 32 }) throw new ArgumentException("invalid collection id", nameof(collectionId));
        if (status is not (null or "prepared_unsold" or "sold" or "listed" or "burned")) throw new ArgumentException("invalid item status", nameof(status));
        var offset = checked((page - 1) * pageSize);
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var tip = await ReadTipAsync(cancellationToken);
        var query = CurrentOutputs();
        if (collectionId != null) query = query.Where(o => o.Collection.ProtocolId == collectionId);
        if (status == "prepared_unsold") query = query.Where(o => o.PurchaseOrigin == null && o.TradeOrigin == null && o.Burn == null);
        if (status == "sold") query = query.Where(o => (o.PurchaseOrigin != null ||
            (o.TradeOrigin != null && o.TradeOrigin.Operation != ListingProofs.ListOperation && o.TradeOrigin.Operation != ListingProofs.ClientListOperation)) && o.Burn == null);
        if (status == "listed") query = query.Where(o => o.TradeOrigin != null &&
            (o.TradeOrigin.Operation == ListingProofs.ListOperation || o.TradeOrigin.Operation == ListingProofs.ClientListOperation) && o.Burn == null);
        if (status == "burned") query = query.Where(o => o.Burn != null);
        var total = await query.LongCountAsync(cancellationToken);
        var rows = await WithDetails(query).OrderByDescending(o => o.Collection.CreationMessage.Transaction.Block.Height)
            .ThenByDescending(o => o.Collection.CreationMessage.Transaction.Position).ThenBy(o => o.RangeStart)
            .Skip(offset).Take(pageSize).ToArrayAsync(cancellationToken);
        var metadata = await ReadMetadataAsync(rows, cancellationToken);
        var primary = await ReadPrimaryAsync(rows, cancellationToken);
        var items = rows.Select(o => Response(o, metadata[o.CollectionId], primary.GetValueOrDefault((o.CollectionId, o.RangeStart!.Value)))).ToArray();
        var spendTip = await ReadTipAsync(cancellationToken, true);
        await snapshot.CommitAsync(cancellationToken);
        return new(options.Value.Network, options.Value.XtopNetwork, tip, page, pageSize, total, items, spendTip);
    }

    public async Task<ItemDetailsResponse?> GetAsync(byte[] itemId, CancellationToken cancellationToken)
    {
        if (itemId.Length != 32) throw new ArgumentException("invalid item id", nameof(itemId));
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var tip = await ReadTipAsync(cancellationToken);
        var output = await WithDetails(CurrentOutputs()).SingleOrDefaultAsync(o => o.ItemId == itemId, cancellationToken);
        if (output == null) return null;
        var metadata = await ReadMetadataAsync([output], cancellationToken);
        var primary = await ReadPrimaryAsync([output], cancellationToken);
        var result = new ItemDetailsResponse(options.Value.Network, options.Value.XtopNetwork, tip,
            Response(output, metadata[output.CollectionId], primary.GetValueOrDefault((output.CollectionId, output.RangeStart!.Value))), await ReadTipAsync(cancellationToken, true));
        await snapshot.CommitAsync(cancellationToken);
        return result;
    }

    private IQueryable<CollectionOutput> CurrentOutputs() => db.CollectionOutputs.AsNoTracking()
        .Where(o => o.Network == options.Value.XtopNetwork && o.Kind == CollectionOutputKind.Item && o.ItemId != null &&
            o.Split == null && o.Purchase == null && o.Trade == null);

    private static IQueryable<CollectionOutput> WithDetails(IQueryable<CollectionOutput> query) => query
        .Include(o => o.Collection)
        .Include(o => o.SourceMessage).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
        .Include(o => o.PurchaseOrigin).ThenInclude(i => i!.Purchase)
        .Include(o => o.TradeOrigin).ThenInclude(t => t!.PreviousOutput)
        .Include(o => o.TradeOrigin).ThenInclude(t => t!.Listing).ThenInclude(t => t!.Message).ThenInclude(m => m.Transaction)
        .Include(o => o.Burn).ThenInclude(b => b!.Transaction).ThenInclude(t => t.Block);

    private async Task<Dictionary<(long, long), PrimaryPurchase>> ReadPrimaryAsync(CollectionOutput[] outputs, CancellationToken cancellationToken)
    {
        var itemIds = outputs.Select(o => o.ItemId!).ToArray();
        if (itemIds.Length == 0) return [];
        var rows = await db.PrimaryPurchaseItems.AsNoTracking().Where(i => itemIds.Contains(i.ItemId) && i.Purchase.Collection.Network == options.Value.XtopNetwork)
            .Include(i => i.Purchase).ThenInclude(p => p.Message).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
            .ToArrayAsync(cancellationToken);
        return rows.ToDictionary(i => (i.CollectionId, i.Serial), i => i.Purchase);
    }

    private async Task<Dictionary<long, ItemMetadataResponse>> ReadMetadataAsync(CollectionOutput[] outputs, CancellationToken cancellationToken)
    {
        var ids = outputs.Select(o => o.CollectionId).Distinct().ToArray();
        if (ids.Length == 0) return [];
        var collections = await db.Collections.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Include(c => c.CreationMessage).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
            .Include(c => c.TermsAttachment).Include(c => c.Outputs.Where(o => o.Kind == CollectionOutputKind.Control))
            .ToArrayAsync(cancellationToken);
        var result = new Dictionary<long, ItemMetadataResponse>();
        foreach (var collection in collections)
        {
            var state = await states.ReadBeforeAsync(collection, long.MaxValue, int.MaxValue, cancellationToken);
            result[collection.Id] = new(state.Metadata.Status,
                state.Metadata.Locations.SingleOrDefault(l => l.Role == 2)?.Uri,
                state.Metadata.Locations.SingleOrDefault(l => l.Role == 3)?.Uri);
        }
        return result;
    }

    private static ItemResponse Response(CollectionOutput output, ItemMetadataResponse metadata, PrimaryPurchase? purchase)
    {
        var transaction = output.SourceMessage.Transaction;
        var block = transaction.Block;
        var primary = purchase?.Message.Transaction;
        var trade = output.TradeOrigin;
        var listing = ListingModes.IsListing(trade?.Operation) ? trade : null;
        var opening = listing ?? trade?.Listing;
        var client = opening?.Operation == ListingProofs.ClientListOperation;
        var burn = output.Burn?.Transaction;
        return new(Hex(output.ItemId!), Hex(output.Collection.ProtocolId), output.RangeStart!.Value,
            burn != null ? "burned" : listing != null ? "listed" : purchase == null ? "prepared_unsold" : "sold",
            Hex(listing?.PreviousOutput.OwnerKey ?? output.OwnerKey),
            new(Hex(transaction.Hash), output.OutputIndex, Hex(output.PublicKey), Hex(output.KeyImage), Atomic(output.NominalAmount),
                block.Height, Hex(block.Hash), block.Timestamp.ToUniversalTime(), 0, output.SourceMessage.Operation == 9 ? (byte)1 : (byte)0),
            purchase == null ? null : new(Hex(primary!.Hash), primary.Block.Height, Hex(primary.Block.Hash), primary.Block.Timestamp.ToUniversalTime(),
                Atomic(output.Collection.PrimaryPrice), purchase.FeeBps, Atomic(decimal.Floor(output.Collection.PrimaryPrice * purchase.FeeBps / 10000))),
            metadata, burn == null ? null : new("external_spend", Hex(burn.Hash), burn.Block.Height, Hex(burn.Block.Hash),
                burn.Position, burn.Block.Timestamp.ToUniversalTime()),
            listing == null || burn != null ? null : new(Hex(transaction.Hash), Atomic(listing.Price!.Value), Hex(listing.PreviousOutput.OwnerKey),
                client ? null : Hex(output.OwnerKey), Payout(listing.SellerPayout!), Payout(listing.ReturnAddress!), client ? null : Payout(listing.ServiceAddress!), listing.FeeBps!.Value,
                ListingModes.Name(listing.Operation), Hex(output.OwnerKey), Payout(listing.ServiceAddress!),
                listing.MarketplaceId == null ? null : Hex(listing.MarketplaceId), listing.MarketplaceConfigHash == null ? null : Hex(listing.MarketplaceConfigHash)),
            trade == null ? null : new(trade.Operation switch
            {
                ListingProofs.ListOperation or ListingProofs.ClientListOperation => "listed", ListingProofs.CancelOperation => "cancelled", SecondaryPurchaseProofs.Operation => "purchased",
                _ => throw new InvalidDataException("invalid stored trade operation")
            }, Hex(transaction.Hash), trade.Listing == null ? null : Hex(trade.Listing.Message.Transaction.Hash),
                trade.SellerAmount == null ? null : Atomic(trade.SellerAmount.Value), trade.RoyaltyAmount == null ? null : Atomic(trade.RoyaltyAmount.Value),
                trade.PlatformFee == null ? null : Atomic(trade.PlatformFee.Value), opening == null ? null : ListingModes.Name(opening.Operation)));
    }

    private async Task<ScannedTipResponse?> ReadTipAsync(CancellationToken cancellationToken, bool spendsOnly = false)
    {
        var tip = await db.Blocks.AsNoTracking().Where(b => b.Network == options.Value.XtopNetwork && (!spendsOnly || b.IsProcessed))
            .OrderByDescending(b => b.Height).Select(b => new { b.Height, b.Hash }).FirstOrDefaultAsync(cancellationToken);
        return tip == null ? null : new(tip.Height, Hex(tip.Hash));
    }

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
    private static PayoutResponse Payout(byte[] address) => new(Hex(address[..32]), Hex(address[32..]));
    private static string Atomic(decimal amount) => amount.ToString("0", CultureInfo.InvariantCulture);
}
