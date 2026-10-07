using System.Data;
using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Models.Listings;
using IndexerCore.Models.Collections;
using IndexerCore.Monero;
using IndexerCore.Protocol.Sales;
using IndexerCore.Services.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static IndexerCore.Services.Items.ItemReadData;

namespace IndexerCore.Services.Listings;

public sealed class ListingQueryService(IndexerDbContext db, ProtocolConfigurationRegistry configurations, IOptions<MoneroOptions> options)
{
    public async Task<ListingListResponse> ListAsync(byte[]? collectionId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (collectionId is { Length: not 32 }) throw new ArgumentException("invalid collection id", nameof(collectionId));
        var offset = Offset(page, pageSize);
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var tip = await Tip(db, options.Value.XtopNetwork, false, cancellationToken);
        var spendTip = await Tip(db, options.Value.XtopNetwork, true, cancellationToken);
        var query = Listings().Where(t => t.SuccessorOutput.Trade == null && t.SuccessorOutput.Burn == null);
        if (collectionId != null) query = query.Where(t => t.PreviousOutput.Collection.ProtocolId == collectionId);
        var total = await query.LongCountAsync(cancellationToken);
        var rows = await Details(query).OrderByDescending(t => t.Message.Transaction.Block.Height)
            .ThenByDescending(t => t.Message.Transaction.Position).ThenByDescending(t => t.MessageId)
            .Skip(offset).Take(pageSize).ToArrayAsync(cancellationToken);
        var result = new ListingListResponse(options.Value.Network, options.Value.XtopNetwork, tip, spendTip,
            page, pageSize, total, rows.Select(t => Response(t, spendTip)).ToArray());
        await snapshot.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ListingDetailsResponse?> GetAsync(byte[] listingId, CancellationToken cancellationToken)
    {
        if (listingId.Length != 32) throw new ArgumentException("invalid listing id", nameof(listingId));
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var row = await Details(Listings()).SingleOrDefaultAsync(t => t.Message.Transaction.Hash == listingId, cancellationToken);
        if (row == null) return null;
        var tip = await Tip(db, options.Value.XtopNetwork, false, cancellationToken);
        var spendTip = await Tip(db, options.Value.XtopNetwork, true, cancellationToken);
        var result = new ListingDetailsResponse(options.Value.Network, options.Value.XtopNetwork, tip, spendTip, Response(row, spendTip));
        await snapshot.CommitAsync(cancellationToken);
        return result;
    }

    private IQueryable<ItemTrade> Listings() => db.ItemTrades.AsNoTracking()
        .Where(t => (t.Operation == ListingProofs.ListOperation || t.Operation == ListingProofs.ClientListOperation) && t.PreviousOutput.Network == options.Value.XtopNetwork);

    private static IQueryable<ItemTrade> Details(IQueryable<ItemTrade> query) => query
        .Include(t => t.Message).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
        .Include(t => t.PreviousOutput).ThenInclude(o => o.Collection)
        .Include(t => t.PreviousOutput).ThenInclude(o => o.SourceMessage).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
        .Include(t => t.SuccessorOutput).ThenInclude(o => o.SourceMessage).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
        .Include(t => t.SuccessorOutput).ThenInclude(o => o.Trade).ThenInclude(t => t!.Message).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
        .Include(t => t.SuccessorOutput).ThenInclude(o => o.Burn).ThenInclude(b => b!.Transaction).ThenInclude(t => t.Block);

    private ListingResponse Response(ItemTrade listing, ScannedTipResponse? spendTip)
    {
        var collection = listing.PreviousOutput.Collection;
        var tx = listing.Message.Transaction;
        var successor = listing.SuccessorOutput;
        var client = listing.Operation == ListingProofs.ClientListOperation;
        var settlement = successor.Trade;
        var status = settlement?.Operation switch
        {
            ListingProofs.CancelOperation => "cancelled",
            SecondaryPurchaseProofs.Operation => "purchased",
            null => successor.Burn == null ? "active" : "burned",
            _ => throw new InvalidDataException("invalid listing settlement")
        };
        var price = listing.Price!.Value;
        var fee = decimal.Floor(price * listing.FeeBps!.Value / 10000);
        var royalty = decimal.Floor(price * collection.RoyaltyBps / 10000);
        var policy = configurations.ResolveListing(listing, collection, tx.Block.Height);
        var confirmations = spendTip == null ? 0 : Math.Max(0, spendTip.Height - tx.Block.Height + 1);
        var remaining = Math.Max(0, 10 - confirmations);
        var resolution = settlement?.Message.Transaction ?? successor.Burn?.Transaction;
        return new(Hex(tx.Hash), Hex(successor.ItemId!), Hex(collection.ProtocolId), successor.RangeStart!.Value, status,
            Atomic(price), listing.FeeBps.Value, Atomic(fee), Atomic(price + fee), Atomic(price - royalty), collection.RoyaltyBps,
            Atomic(royalty), Hex(listing.PreviousOutput.OwnerKey), client ? null : Hex(successor.OwnerKey), Payout(listing.SellerPayout!),
            Payout(listing.ReturnAddress!), client ? null : Payout(listing.ServiceAddress!), Payout(collection.RoyaltyPayout),
            Payout([.. policy.FeeSpendKey, .. policy.FeeViewKey]), Output(successor), confirmations, remaining,
            status == "active" && remaining == 0, resolution == null ? null : Transaction(resolution),
            listing.MarketplaceId == null ? null : Hex(listing.MarketplaceId),
            listing.MarketplaceConfigHash == null ? null : Hex(listing.MarketplaceConfigHash),
            ListingModes.Name(listing.Operation), Hex(successor.OwnerKey), Payout(listing.ServiceAddress!))
        {
            PreviousOutput = Output(listing.PreviousOutput)
        };
    }
}
