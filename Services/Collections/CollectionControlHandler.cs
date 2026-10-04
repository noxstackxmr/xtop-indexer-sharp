using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Protocol.Attachments;
using IndexerCore.Protocol.Collections;
using IndexerCore.Protocol.Messages;
using IndexerCore.Services.Attachments;
using IndexerCore.Services.Indexing;
using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Services.Collections;

public sealed class CollectionControlHandler(IndexerDbContext db, CollectionStateService states,
    AttachmentService attachments, ProtocolConfigurationRegistry configurations, NativeTransactionReader transactions)
{
    public async Task HandleAsync(Message message, XtopMessage envelope, CancellationToken cancellationToken)
    {
        var transaction = message.Transaction;
        var block = transaction.Block;
        var policy = configurations.Resolve(block.Network, envelope.ConfigHash, block.Height);
        var step = CollectionControlReader.Read(envelope);
        var collection = await db.Collections
            .Include(c => c.CreationMessage).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
            .Include(c => c.TermsAttachment).Include(c => c.Outputs.Where(o => o.Kind == CollectionOutputKind.Control))
            .SingleOrDefaultAsync(c => c.Network == block.Network && c.ProtocolId == step.CollectionId, cancellationToken)
            ?? throw new FormatException("collection does not exist");
        var creation = collection.CreationMessage.Transaction;
        if (creation.Block.Height > block.Height || (creation.Block.Height == block.Height && creation.Position >= transaction.Position))
            throw new FormatException("collection must precede its management operation");
        if (!collection.ConfigHash.AsSpan().SequenceEqual(policy.ConfigHash))
            throw new FormatException("collection configuration mismatch");

        var previous = await states.ReadBeforeAsync(collection, block.Height, transaction.Position, cancellationToken);
        var native = await transactions.ReadAsync(message, cancellationToken);
        step = CollectionControlProofs.Verify(transaction.NativeData!, policy, previous.Control);
        if (await db.CollectionChanges.AnyAsync(c => c.MessageId == message.TransactionId, cancellationToken)) return;
        if (await db.CollectionChanges.AnyAsync(c => c.Network == block.Network && c.PreviousKeyImage == step.PreviousKeyImage, cancellationToken))
            throw new FormatException("control has already been consumed");
        if (await db.CollectionOutputs.AnyAsync(o => o.Network == block.Network && o.KeyImage == step.Successor.KeyImage, cancellationToken) ||
            await db.CollectionChanges.AnyAsync(c => c.Network == block.Network && c.KeyImage == step.Successor.KeyImage, cancellationToken))
            throw new FormatException("output key image is already bound");
        foreach (var image in native.InputKeyImages.Where(i => !i.AsSpan().SequenceEqual(step.PreviousKeyImage)))
            if (await db.CollectionOutputs.AnyAsync(o => o.Network == block.Network && o.KeyImage == image, cancellationToken) ||
                await db.CollectionChanges.AnyAsync(c => c.Network == block.Network && c.KeyImage == image, cancellationToken))
                throw new FormatException("management must not spend another bound output");

        Attachment? locationsAttachment = null;
        MediaLocation[] replacements = [];
        if (step.Locations is { } reference)
        {
            locationsAttachment = await db.Attachments.SingleOrDefaultAsync(a => a.Network == block.Network &&
                a.Hash == reference.Hash && a.TotalLength == reference.TotalLength && a.MerkleRoot == reference.MerkleRoot, cancellationToken)
                ?? throw new FormatException("missing preceding collection locations");
            var data = await attachments.ReadBeforeAsync(locationsAttachment, block.Height, transaction.Position, policy.ConfigHash, cancellationToken)
                ?? throw new FormatException("collection locations are incomplete before use");
            replacements = MediaLocationsReader.Read(data);
        }
        var metadata = previous.Metadata.Apply(envelope.Operation, replacements);
        db.CollectionChanges.Add(new CollectionChange
        {
            MessageId = message.TransactionId, Message = message, Collection = collection,
            Network = block.Network, Operation = envelope.Operation, PreviousKeyImage = step.PreviousKeyImage,
            OutputIndex = step.Successor.OutputIndex, PublicKey = native.Outputs[step.Successor.OutputIndex].Key,
            KeyImage = step.Successor.KeyImage, OwnerKey = step.Successor.OwnerKey, NominalAmount = step.Successor.NominalAmount,
            LocationsAttachment = locationsAttachment, Revealed = metadata.Revealed,
            CollectionMetadataUri = metadata.Locations.Single(l => l.Role == 1).Uri,
            ItemsMetadataUri = metadata.Locations.SingleOrDefault(l => l.Role == 2)?.Uri,
            PlaceholderUri = metadata.Locations.SingleOrDefault(l => l.Role == 3)?.Uri
        });
    }
}
