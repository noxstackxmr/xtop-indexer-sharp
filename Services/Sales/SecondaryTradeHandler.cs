using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Protocol.Collections;
using IndexerCore.Protocol.Messages;
using IndexerCore.Protocol.Sales;
using IndexerCore.Services.Indexing;
using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Services.Sales;

public sealed class SecondaryTradeHandler(IndexerDbContext db, ProtocolConfigurationRegistry configurations, NativeTransactionReader transactions)
{
    public async Task HandleAsync(Message message, XtopMessage envelope, CancellationToken cancellationToken)
    {
        var profile = envelope.Operation switch
        {
            ListingProofs.ListOperation => ListingProofs.ListProfile,
            ListingProofs.CancelOperation => ListingProofs.CancelProfile,
            SecondaryPurchaseProofs.Operation => SecondaryPurchaseProofs.Profile,
            _ => throw new FormatException("invalid secondary operation")
        };
        if (envelope.Witnesses.Length != 1 || envelope.Witnesses[0].Kind != 7) throw new FormatException("secondary proof is required");
        if (envelope.Witnesses[0].Profile != profile) throw new NotSupportedException("secondary proof profile is not supported");
        var transaction = message.Transaction; var block = transaction.Block;
        var policy = configurations.Resolve(block.Network, envelope.ConfigHash, block.Height);
        var feeBps = configurations.ResolveSecondaryFee(block.Network, envelope.ConfigHash, block.Height);
        var native = await transactions.ReadAsync(message, cancellationToken);
        var sources = new List<CollectionOutput>();
        foreach (var image in native.InputKeyImages)
        {
            if (await db.CollectionChanges.AnyAsync(c => c.Network == block.Network && c.KeyImage == image, cancellationToken))
                throw new FormatException("trade must not spend a management output");
            var output = await db.CollectionOutputs.Include(o => o.Collection)
                .Include(o => o.SourceMessage).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
                .Include(o => o.Purchase).Include(o => o.PurchaseOrigin).Include(o => o.Burn).Include(o => o.Trade)
                .Include(o => o.TradeOrigin).ThenInclude(t => t!.PreviousOutput).ThenInclude(o => o.SourceMessage)
                .SingleOrDefaultAsync(o => o.Network == block.Network && o.KeyImage == image, cancellationToken);
            if (output != null) sources.Add(output);
        }
        if (sources.Count != 1) throw new FormatException("trade must spend exactly one bound item and one ordinary money input");
        var previous = sources[0]; var collection = previous.Collection;
        if (previous.Kind != CollectionOutputKind.Item || previous.ItemId == null || previous.RangeStart == null ||
            previous.RangeStart < 0 || previous.RangeEnd != previous.RangeStart + 1 || previous.RangeEnd > collection.MaxSupply ||
            previous.Purchase != null || (previous.PurchaseOrigin == null && previous.TradeOrigin == null) ||
            (previous.Trade != null && previous.Trade.MessageId != message.TransactionId) ||
            (previous.Burn != null && previous.Burn.TransactionId != message.TransactionId))
            throw new FormatException("trade requires an available purchased item");
        if (!collection.ConfigHash.AsSpan().SequenceEqual(policy.ConfigHash)) throw new FormatException("secondary collection configuration mismatch");
        var origin = previous.SourceMessage.Transaction;
        if (origin.Block.Height >= block.Height) throw new FormatException("trade must follow the source block");
        var item = Item(previous, collection.ProtocolId);
        if (!previous.ItemId.AsSpan().SequenceEqual(ListingProofs.ItemId(item))) throw new InvalidDataException("stored item identity mismatch");
        var opening = previous.TradeOrigin is { Operation: ListingProofs.ListOperation } ? previous.TradeOrigin : null;
        NewBinding next;
        ListedTerms? terms = null;
        SecondaryPayment[]? payments = null;
        if (envelope.Operation == ListingProofs.ListOperation)
        {
            if (opening != null) throw new FormatException("item is already listed");
            var result = ListingProofs.VerifyList(native, policy, item);
            next = result.Successor; terms = result.Terms;
        }
        else
        {
            if (opening == null) throw new FormatException("item must have an active service listing");
            var listing = new ListingState(Item(opening.PreviousOutput, collection.ProtocolId), new(checked((ulong)opening.Price!), opening.SellerPayout!,
                opening.ReturnAddress!, opening.ServiceAddress!), item.Binding, item.PublicKey, origin.Hash);
            if (envelope.Operation == ListingProofs.CancelOperation)
                next = ListingProofs.VerifyCancel(native, policy, listing).Successor;
            else
            {
                var fees = new SecondaryFeePolicy(checked((ushort)collection.RoyaltyBps), collection.RoyaltyPayout, feeBps);
                next = SecondaryPurchaseProofs.Verify(native, policy, listing, fees).Successor;
                payments = SecondaryPurchaseProofs.Payments(policy, listing.Terms, fees);
            }
        }
        if (await db.ItemTrades.AnyAsync(t => t.MessageId == message.TransactionId, cancellationToken)) return;
        if (await db.CollectionOutputs.AnyAsync(o => o.Network == block.Network && o.KeyImage == next.KeyImage, cancellationToken) ||
            await db.CollectionChanges.AnyAsync(c => c.Network == block.Network && c.KeyImage == next.KeyImage, cancellationToken))
            throw new FormatException("successor key image is already bound");
        var successor = new CollectionOutput
        {
            Collection = collection, SourceMessage = message, Network = block.Network, Kind = CollectionOutputKind.Item,
            ItemId = previous.ItemId, OutputIndex = next.OutputIndex, PublicKey = native.Outputs[next.OutputIndex].Key,
            KeyImage = next.KeyImage, OwnerKey = next.OwnerKey, NominalAmount = next.NominalAmount,
            RangeStart = previous.RangeStart, RangeEnd = previous.RangeEnd
        };
        decimal? Paid(byte role) => payments == null ? null : payments.SingleOrDefault(p => p.Role == role)?.Amount ?? 0UL;
        db.ItemTrades.Add(new ItemTrade
        {
            Message = message, Operation = envelope.Operation, PreviousOutput = previous, SuccessorOutput = successor,
            Listing = opening, Price = terms?.Price, SellerPayout = terms?.SellerPayout, ReturnAddress = terms?.ReturnAddress,
            ServiceAddress = terms?.ServiceAddress, FeeBps = feeBps,
            SellerAmount = Paid(4), RoyaltyAmount = Paid(2), PlatformFee = Paid(3)
        });
        if (previous.Burn != null) db.ItemBurns.Remove(previous.Burn);
    }

    private static ListingItem Item(CollectionOutput output, byte[] collectionId)
    {
        var original = XtopMessageReader.ReadMessage(output.SourceMessage.Data, output.Network);
        var amountWitness = original.Operation == 9 ? (byte)1 : (byte)0;
        var binding = new NewBinding(output.OutputIndex, output.KeyImage, output.OwnerKey, checked((ulong)output.NominalAmount), 0, amountWitness);
        return new(collectionId, checked((uint)output.RangeStart!.Value), binding, output.PublicKey);
    }
}
