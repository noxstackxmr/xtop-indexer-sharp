using System.Data;
using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Models.Items;
using IndexerCore.Monero;
using IndexerCore.Protocol.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static IndexerCore.Services.Items.ItemReadData;

namespace IndexerCore.Services.Items;

public sealed class ItemHistoryQueryService(IndexerDbContext db, IOptions<MoneroOptions> options)
{
    public async Task<ItemHistoryResponse?> GetAsync(byte[] itemId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (itemId.Length != 32) throw new ArgumentException("invalid item id", nameof(itemId));
        var offset = Offset(page, pageSize);
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var outputs = db.CollectionOutputs.AsNoTracking().Where(o => o.Network == options.Value.XtopNetwork &&
            o.Kind == CollectionOutputKind.Item && o.ItemId == itemId);
        var identity = await outputs.Select(o => new { o.Collection.ProtocolId, Serial = o.RangeStart!.Value }).FirstOrDefaultAsync(cancellationToken);
        if (identity == null) return null;
        var tip = await Tip(db, options.Value.XtopNetwork, false, cancellationToken);
        var spendTip = await Tip(db, options.Value.XtopNetwork, true, cancellationToken);
        var events = outputs.Select(o => new HistoryRow { OutputId = o.Id, Height = o.SourceMessage.Transaction.Block.Height,
            Position = o.SourceMessage.Transaction.Position, Burn = false })
            .Concat(outputs.Where(o => o.Burn != null).Select(o => new HistoryRow { OutputId = o.Id,
                Height = o.Burn!.Transaction.Block.Height, Position = o.Burn.Transaction.Position, Burn = true }));
        var total = await events.LongCountAsync(cancellationToken);
        var rows = await events.OrderByDescending(e => e.Height).ThenByDescending(e => e.Position)
            .ThenByDescending(e => e.Burn).ThenByDescending(e => e.OutputId).Skip(offset).Take(pageSize).ToArrayAsync(cancellationToken);
        var ids = rows.Select(r => r.OutputId).Distinct().ToArray();
        var details = await outputs.Where(o => ids.Contains(o.Id))
            .Include(o => o.Collection)
            .Include(o => o.SourceMessage).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
            .Include(o => o.Burn).ThenInclude(b => b!.Transaction).ThenInclude(t => t.Block)
            .Include(o => o.PurchaseOrigin).ThenInclude(i => i!.Purchase)
            .Include(o => o.PurchaseOrigin).ThenInclude(i => i!.PreviousOutput).ThenInclude(o => o.SourceMessage).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
            .Include(o => o.TradeOrigin).ThenInclude(t => t!.PreviousOutput).ThenInclude(o => o.SourceMessage).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
            .Include(o => o.TradeOrigin).ThenInclude(t => t!.Listing).ThenInclude(l => l!.PreviousOutput)
            .Include(o => o.TradeOrigin).ThenInclude(t => t!.Listing).ThenInclude(l => l!.Message).ThenInclude(m => m.Transaction)
            .ToDictionaryAsync(o => o.Id, cancellationToken);
        var result = new ItemHistoryResponse(options.Value.Network, options.Value.XtopNetwork, tip, spendTip, Hex(itemId),
            Hex(identity.ProtocolId), identity.Serial, page, pageSize, total, rows.Select(r => Response(details[r.OutputId], r.Burn)).ToArray());
        await snapshot.CommitAsync(cancellationToken);
        return result;
    }

    private static ItemHistoryEventResponse Response(CollectionOutput output, bool burn)
    {
        var trade = output.TradeOrigin;
        var primary = output.PurchaseOrigin;
        var listed = ListingModes.IsListing(trade?.Operation);
        var opening = listed ? trade : trade?.Listing;
        var mode = opening == null ? null : ListingModes.Name(opening.Operation);
        var client = opening?.Operation == ListingProofs.ClientListOperation;
        var owner = listed ? trade!.PreviousOutput.OwnerKey : output.OwnerKey;
        if (burn)
            return new("burned", Transaction(output.Burn!.Transaction), Hex(owner), null,
                listed && !client ? Hex(output.OwnerKey) : null, listed ? Hex(output.SourceMessage.Transaction.Hash) : null,
                Output(output), null, null, null, listed ? mode : null, listed ? Hex(output.OwnerKey) : null);
        var type = trade?.Operation switch
        {
            ListingProofs.ListOperation or ListingProofs.ClientListOperation => "listed",
            ListingProofs.CancelOperation => "cancelled",
            SecondaryPurchaseProofs.Operation => "secondary_purchase",
            null => primary == null ? "prepared" : "primary_purchase",
            _ => throw new InvalidDataException("invalid item history operation")
        };
        var previous = trade?.PreviousOutput ?? primary?.PreviousOutput;
        var from = trade?.Listing?.PreviousOutput.OwnerKey ?? previous?.OwnerKey;
        var service = trade == null ? null : listed ? output.OwnerKey : trade.PreviousOutput.OwnerKey;
        var listingId = trade == null ? null : listed ? output.SourceMessage.Transaction.Hash : trade.Listing!.Message.Transaction.Hash;
        decimal? price = trade == null ? primary == null ? null : output.Collection.PrimaryPrice : (trade.Price ?? trade.Listing!.Price);
        ItemHistoryPaymentsResponse? payments = null;
        if (primary != null)
        {
            var fee = decimal.Floor(price!.Value * primary.Purchase.FeeBps / 10000);
            payments = new(Atomic(price.Value), "0", primary.Purchase.FeeBps, Atomic(fee), Atomic(price.Value + fee));
        }
        else if (trade?.Operation == SecondaryPurchaseProofs.Operation)
            payments = new(Atomic(trade.SellerAmount!.Value), Atomic(trade.RoyaltyAmount!.Value), trade.FeeBps!.Value,
                Atomic(trade.PlatformFee!.Value), Atomic(price!.Value + trade.PlatformFee.Value));
        return new(type, Transaction(output.SourceMessage.Transaction), from == null ? null : Hex(from), Hex(owner),
            service == null || client ? null : Hex(service), listingId == null ? null : Hex(listingId),
            previous == null ? null : Output(previous), Output(output), price == null ? null : Atomic(price.Value), payments,
            mode, service == null ? null : Hex(service));
    }

    private sealed class HistoryRow
    {
        public long OutputId { get; init; }
        public long Height { get; init; }
        public int Position { get; init; }
        public bool Burn { get; init; }
    }
}
