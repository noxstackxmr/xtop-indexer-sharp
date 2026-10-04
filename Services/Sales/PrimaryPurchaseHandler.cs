using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Protocol.Attachments;
using IndexerCore.Protocol.Collections;
using IndexerCore.Protocol.Issuance;
using IndexerCore.Protocol.Messages;
using IndexerCore.Protocol.Sales;
using IndexerCore.Services.Indexing;
using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Services.Sales;

public sealed class PrimaryPurchaseHandler(IndexerDbContext db, ProtocolConfigurationRegistry configurations, NativeTransactionReader transactions)
{
    public async Task HandleAsync(Message message, XtopMessage envelope, CancellationToken cancellationToken)
    {
        var batch = envelope.Operation == PrimaryBatchProofs.Operation;
        if (!batch)
        {
            if (envelope.Operation != 9 || envelope.Payload.Length == 0 || envelope.Payload[0] > 2)
                throw new FormatException("invalid SALE mode");
            if (envelope.Payload[0] != 2) throw new NotSupportedException("secondary sales are not supported");
        }
        var profile = batch ? PrimaryBatchProofs.Profile : PrimarySaleProofs.Profile;
        if (envelope.Witnesses.Length == 0) throw new FormatException("primary proofs are required");
        if (envelope.Witnesses.Any(w => w.Profile != profile)) throw new NotSupportedException("primary proof profile is not supported");
        var transaction = message.Transaction; var block = transaction.Block;
        var configuration = configurations.Resolve(block.Network, envelope.ConfigHash, block.Height);
        var feeBps = configurations.ResolvePrimaryFee(block.Network, envelope.ConfigHash, block.Height);
        var native = await transactions.ReadAsync(message, cancellationToken);
        var sources = new List<CollectionOutput>();
        foreach (var image in native.InputKeyImages)
        {
            if (await db.CollectionChanges.AnyAsync(c => c.Network == block.Network && c.KeyImage == image, cancellationToken))
                throw new FormatException("purchase must not spend a management output");
            var source = await db.CollectionOutputs.Include(o => o.Collection).ThenInclude(c => c.TermsAttachment)
                .Include(o => o.SourceMessage).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
                .Include(o => o.Purchase).Include(o => o.PurchaseOrigin)
                .SingleOrDefaultAsync(o => o.Network == block.Network && o.KeyImage == image, cancellationToken);
            if (source == null) continue;
            if (source.Kind != CollectionOutputKind.Nft || source.RangeStart == null || source.RangeEnd != source.RangeStart + 1 ||
                source.RangeStart < 0 || source.RangeEnd > source.Collection.MaxSupply || source.PurchaseOrigin != null ||
                (source.Purchase != null && source.Purchase.PurchaseMessageId != message.TransactionId))
                throw new FormatException("primary purchase requires prepared unsold NFT outputs");
            var origin = source.SourceMessage.Transaction;
            if (origin.Block.Height > block.Height || (origin.Block.Height == block.Height && origin.Position >= transaction.Position))
                throw new FormatException("NFT output must precede its purchase");
            if (!source.Collection.ConfigHash.AsSpan().SequenceEqual(configuration.ConfigHash))
                throw new FormatException("primary collection configuration mismatch");
            sources.Add(source);
        }
        if (sources.Count is < 1 or > 5 || (!batch && sources.Count != 1) || sources.Select(s => s.CollectionId).Distinct().Count() != 1)
            throw new FormatException("purchase must consume NFTs of one collection");
        sources = sources.OrderBy(s => s.RangeStart).ToList();
        if (sources.Select(s => s.RangeStart).Distinct().Count() != sources.Count) throw new FormatException("duplicate primary serial");
        var policies = sources.Select(s => new PrimarySalePolicy(configuration, s.Collection.ProtocolId, checked((uint)s.RangeStart!.Value),
            OriginalBinding(s), s.PublicKey, new ChunkReference(s.Collection.TermsAttachment.Hash, checked((uint)s.Collection.TermsAttachment.TotalLength),
                s.Collection.TermsAttachment.MerkleRoot), s.Collection.PrimaryPayout, checked((ulong)s.Collection.PrimaryPrice), feeBps)).ToArray();
        NewBinding[] buyers; ulong creatorAmount, platformFee;
        if (batch)
        {
            var result = PrimaryBatchProofs.Verify(native, policies);
            buyers = result.Buyers; creatorAmount = result.CreatorAmount; platformFee = result.PlatformFee;
        }
        else
        {
            var result = PrimarySaleProofs.Verify(native, policies[0]);
            buyers = [result.Buyer]; creatorAmount = result.CreatorAmount; platformFee = result.PlatformFee;
        }
        if (await db.PrimaryPurchases.AnyAsync(p => p.MessageId == message.TransactionId, cancellationToken)) return;
        foreach (var source in sources)
            if (await db.PrimaryPurchaseItems.AnyAsync(i => i.CollectionId == source.CollectionId && i.Serial == source.RangeStart, cancellationToken))
                throw new FormatException("NFT has already completed a primary purchase");
        foreach (var buyer in buyers)
            if (await db.CollectionOutputs.AnyAsync(o => o.Network == block.Network && o.KeyImage == buyer.KeyImage, cancellationToken) ||
                await db.CollectionChanges.AnyAsync(c => c.Network == block.Network && c.KeyImage == buyer.KeyImage, cancellationToken))
                throw new FormatException("buyer key image is already bound");

        var purchase = new PrimaryPurchase { Message = message, Collection = sources[0].Collection, Profile = profile,
            FeeBps = feeBps, CreatorAmount = creatorAmount, PlatformFee = platformFee };
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i]; var buyer = buyers[i];
            var successor = new CollectionOutput
            {
                Collection = source.Collection, SourceMessage = message, Network = block.Network, Kind = CollectionOutputKind.Nft,
                OutputIndex = buyer.OutputIndex, PublicKey = native.Outputs[buyer.OutputIndex].Key, KeyImage = buyer.KeyImage,
                OwnerKey = buyer.OwnerKey, NominalAmount = buyer.NominalAmount, RangeStart = source.RangeStart, RangeEnd = source.RangeEnd
            };
            purchase.Items.Add(new PrimaryPurchaseItem { CollectionId = source.CollectionId, Serial = source.RangeStart!.Value,
                ItemId = PrimarySaleProofs.ItemId(source.Collection.ProtocolId, checked((uint)source.RangeStart.Value)),
                PreviousOutput = source, BuyerOutput = successor });
        }
        db.PrimaryPurchases.Add(purchase);
    }

    private static NewBinding OriginalBinding(CollectionOutput output)
    {
        var message = XtopMessageReader.ReadMessage(output.SourceMessage.Data, output.Network);
        if (message.Operation == 2)
        {
            var root = CollectionCreateReader.Read(message).IssuanceRoot;
            if (root.OutputIndex != output.OutputIndex || !root.KeyImage.AsSpan().SequenceEqual(output.KeyImage))
                throw new InvalidDataException("stored NFT root differs from its source");
            return root;
        }
        if (IssueSplitReader.ReadProfile(message) == CompactIssueSplitProofs.Profile)
            return new(output.OutputIndex, output.KeyImage, output.OwnerKey, checked((ulong)output.NominalAmount), 0, 0);
        return IssueSplitReader.Read(message).Children.Single(c => c.Binding.OutputIndex == output.OutputIndex).Binding;
    }
}
