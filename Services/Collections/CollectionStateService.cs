using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Protocol.Attachments;
using IndexerCore.Protocol.Collections;
using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Services.Collections;

public sealed record ResolvedCollectionState(CollectionControlState Control, CollectionMetadataState Metadata,
    CollectionChange? LastChange);

public sealed class CollectionStateService(IndexerDbContext db, CollectionTermsService termsService)
{
    public async Task<ResolvedCollectionState> ReadBeforeAsync(Collection collection, long height, int position,
        CancellationToken cancellationToken)
    {
        var change = await db.CollectionChanges.AsNoTracking()
            .Include(c => c.LocationsAttachment)
            .Include(c => c.Message).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
            .Where(c => c.CollectionId == collection.Id &&
                (c.Message.Transaction.Block.Height < height ||
                 (c.Message.Transaction.Block.Height == height && c.Message.Transaction.Position < position)))
            .OrderByDescending(c => c.Message.Transaction.Block.Height).ThenByDescending(c => c.Message.Transaction.Position)
            .FirstOrDefaultAsync(cancellationToken);
        if (change != null)
        {
            List<MediaLocation> locations = [new(1, change.CollectionMetadataUri)];
            if (change.ItemsMetadataUri != null) locations.Add(new(2, change.ItemsMetadataUri));
            if (change.PlaceholderUri != null) locations.Add(new(3, change.PlaceholderUri));
            return new(new(collection.ProtocolId,
                    new(change.OutputIndex, change.KeyImage, change.OwnerKey, checked((ulong)change.NominalAmount), 0, 1), change.PublicKey),
                new(collection.MetadataMode, change.Revealed, locations.ToArray()), change);
        }
        var creation = collection.CreationMessage.Transaction;
        var resolved = await termsService.ReadBeforeAsync(collection.TermsAttachment, creation.Block.Height, creation.Position,
            collection.ConfigHash, cancellationToken)
            ?? throw new InvalidDataException("stored collection dependencies are incomplete");
        if (resolved.LocationsAttachment.Id != collection.LocationsAttachmentId)
            throw new InvalidDataException("stored collection locations do not match its terms");
        var output = collection.Outputs.Single(o => o.Kind == CollectionOutputKind.Control);
        return new(new(collection.ProtocolId,
                new(output.OutputIndex, output.KeyImage, output.OwnerKey, checked((ulong)output.NominalAmount), 0, 1), output.PublicKey),
            new(collection.MetadataMode, false, resolved.Locations), null);
    }
}
