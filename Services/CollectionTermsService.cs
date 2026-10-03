using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Protocol;
using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Services;

public sealed record ResolvedCollectionTerms(CollectionTerms Terms, Attachment LocationsAttachment, MediaLocation[] Locations);

public sealed class CollectionTermsService(IndexerDbContext db, AttachmentService attachments)
{
    public async Task<ResolvedCollectionTerms?> ReadBeforeAsync(Attachment termsAttachment, long height, int position,
        CancellationToken cancellationToken)
        => await ReadBeforeAsync(termsAttachment, height, position, null, cancellationToken);

    public async Task<ResolvedCollectionTerms?> ReadBeforeAsync(Attachment termsAttachment, long height, int position,
        byte[]? configHash, CancellationToken cancellationToken)
    {
        var message = await attachments.ReadBeforeAsync(termsAttachment, height, position, configHash, cancellationToken);
        if (message == null) return null;
        var terms = CollectionTermsReader.Read(message);
        var reference = terms.InitialLocations;
        var locationsAttachment = db.Attachments.Local.FirstOrDefault(a =>
            a.Network == termsAttachment.Network && a.TotalLength == reference.TotalLength &&
            a.Hash.AsSpan().SequenceEqual(reference.Hash) && a.MerkleRoot.AsSpan().SequenceEqual(reference.MerkleRoot));
        locationsAttachment ??= await db.Attachments.SingleOrDefaultAsync(a =>
            a.Network == termsAttachment.Network && a.TotalLength == reference.TotalLength &&
            a.Hash == reference.Hash && a.MerkleRoot == reference.MerkleRoot, cancellationToken);
        if (locationsAttachment == null) return null;
        var locationsMessage = await attachments.ReadBeforeAsync(locationsAttachment, height, position, configHash, cancellationToken);
        if (locationsMessage == null) return null;
        var locations = MediaLocationsReader.Read(locationsMessage);
        var expectedRole = terms.MetadataMode == 0 ? 2 : 3;
        if (locations.Length != 2 || locations[0].Role != 1 || locations[1].Role != expectedRole)
            throw new FormatException("initial locations do not match the metadata mode");
        return new ResolvedCollectionTerms(terms, locationsAttachment, locations);
    }
}
