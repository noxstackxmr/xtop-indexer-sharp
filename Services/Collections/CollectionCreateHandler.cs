using IndexerCore.Protocol.Messages;
using IndexerCore.Protocol.Collections;
using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Monero;
using IndexerCore.Services.Indexing;
using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Services.Collections;

public sealed class CollectionCreateHandler(IndexerDbContext db, CollectionTermsService termsService,
    ProtocolConfigurationRegistry configurations, NativeTransactionReader transactions)
{
    public async Task HandleAsync(Message message, XtopMessage envelope, CancellationToken cancellationToken)
    {
        var transaction = message.Transaction;
        var block = transaction.Block;
        var policy = configurations.Resolve(block.Network, envelope.ConfigHash, block.Height);
        if (envelope.Witnesses.Any(w => w.Profile != 0 && w.Profile != CollectionCreateProofs.Profile))
            throw new NotSupportedException("creation proof profile is not supported");
        var create = CollectionCreateReader.Read(envelope);
        var reference = create.Terms;
        var termsAttachment = await db.Attachments.SingleOrDefaultAsync(a => a.Network == block.Network &&
            a.Hash == reference.Hash && a.TotalLength == reference.TotalLength && a.MerkleRoot == reference.MerkleRoot, cancellationToken);
        if (termsAttachment == null) throw new FormatException("missing preceding collection terms");
        var resolved = await termsService.ReadBeforeAsync(termsAttachment, block.Height, transaction.Position,
            policy.ConfigHash, cancellationToken);
        if (resolved == null) throw new FormatException("collection attachments are incomplete before creation");
        var terms = resolved.Terms;
        foreach (var key in new[] { terms.PrimaryPayout.PublicSpendKey, terms.PrimaryPayout.PublicViewKey,
                     terms.RoyaltyPayout.PublicSpendKey, terms.RoyaltyPayout.PublicViewKey })
            MoneroProofCrypto.RequirePoint(key);

        var native = await transactions.ReadAsync(message, cancellationToken);
        create = CollectionCreateProofs.Verify(transaction.NativeData!, policy, reference, terms.MaxSupply);

        if (await db.Collections.AnyAsync(c => c.CreationMessageId == message.TransactionId, cancellationToken)) return;
        if (await db.Collections.AnyAsync(c => c.Network == block.Network && c.ProtocolId == transaction.Hash, cancellationToken))
            throw new FormatException("collection already exists");
        foreach (var binding in new[] { create.Control, create.IssuanceRoot })
            if (await db.CollectionOutputs.AnyAsync(o => o.Network == block.Network && o.KeyImage == binding.KeyImage, cancellationToken) ||
                await db.CollectionChanges.AnyAsync(c => c.Network == block.Network && c.KeyImage == binding.KeyImage, cancellationToken))
                throw new FormatException("output key image is already bound");

        var collection = new Collection
        {
            Network = block.Network, ProtocolId = [.. transaction.Hash], ConfigHash = [.. policy.ConfigHash],
            CreationMessageId = message.TransactionId, CreationMessage = message,
            TermsAttachment = termsAttachment, LocationsAttachment = resolved.LocationsAttachment,
            Name = terms.Name, MaxSupply = terms.MaxSupply, MetadataMode = terms.MetadataMode,
            ManagerPermissions = terms.ManagerPermissions,
            PrimaryPayout = [.. terms.PrimaryPayout.PublicSpendKey, .. terms.PrimaryPayout.PublicViewKey],
            RoyaltyPayout = [.. terms.RoyaltyPayout.PublicSpendKey, .. terms.RoyaltyPayout.PublicViewKey],
            RoyaltyBps = terms.RoyaltyBps, PrimaryPrice = terms.PrimaryPrice,
            SaleStartUtc = DateTimeOffset.FromUnixTimeSeconds((long)terms.SaleStartUtc)
        };
        CollectionOutput Output(NewBinding binding, CollectionOutputKind kind) => new()
        {
            Collection = collection, Network = block.Network, Kind = kind, OutputIndex = binding.OutputIndex,
            PublicKey = native.Outputs[binding.OutputIndex].Key, KeyImage = binding.KeyImage, OwnerKey = binding.OwnerKey,
            NominalAmount = binding.NominalAmount, RangeStart = kind == CollectionOutputKind.Control ? null : 0,
            RangeEnd = kind == CollectionOutputKind.Control ? null : terms.MaxSupply
        };
        collection.Outputs.Add(Output(create.Control, CollectionOutputKind.Control));
        collection.Outputs.Add(Output(create.IssuanceRoot, create.IssuanceRootKind == 0 ? CollectionOutputKind.Issuance : CollectionOutputKind.Nft));
        db.Collections.Add(collection);
    }
}
