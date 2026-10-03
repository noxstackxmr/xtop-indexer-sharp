using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Protocol;
using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Services;

public sealed class AttachmentService(IndexerDbContext db)
{
    public Task<List<Attachment>> GetReadyAsync(byte network, CancellationToken cancellationToken)
        => db.Attachments.Where(a => a.Network == network && a.Status == AttachmentStatus.Incomplete && a.Chunks.Any() &&
                                     (a.Chunks.Select(c => c.Index).Distinct().Count() == a.Chunks.Max(c => c.Count) ||
                                      a.Chunks.Min(c => c.Count) != a.Chunks.Max(c => c.Count)))
            .OrderBy(a => a.Id).Take(100).ToListAsync(cancellationToken);

    public async Task RefreshAsync(Attachment attachment, CancellationToken cancellationToken)
    {
        try
        {
            var message = await ReadAsync(attachment, null, 0, cancellationToken);
            attachment.Status = message == null ? AttachmentStatus.Incomplete : AttachmentStatus.Complete;
            attachment.Type = message?.Operation;
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException)
        {
            attachment.Status = AttachmentStatus.Invalid;
            attachment.Type = null;
        }
    }

    public Task<XtopMessage?> ReadBeforeAsync(Attachment attachment, long height, int position, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        return ReadAsync(attachment, height, position, cancellationToken);
    }

    private async Task<XtopMessage?> ReadAsync(Attachment attachment, long? height, int position, CancellationToken cancellationToken)
    {
        if (attachment.TotalLength is < 1 or > 16_777_216)
            throw new FormatException("invalid attachment length");
        var query = db.DataChunks.Where(c => c.AttachmentId == attachment.Id);
        if (height.HasValue)
            query = query.Where(c => c.Message.Transaction.Block.Height < height.Value ||
                                     (c.Message.Transaction.Block.Height == height.Value && c.Message.Transaction.Position < position));

        var publications = db.Entry(attachment).State == EntityState.Added
            ? []
            : await query.GroupBy(c => new { c.Index, c.Count })
                .Select(g => new Publication(g.Min(c => c.MessageId), g.Key.Index, g.Key.Count))
                .ToListAsync(cancellationToken);
        var additions = db.DataChunks.Local.Where(c => ReferenceEquals(c.Attachment, attachment) &&
                                                       db.Entry(c).State == EntityState.Added).ToList();
        if (height.HasValue && additions.Count > 0)
        {
            var ids = additions.Select(c => c.MessageId).ToArray();
            var precedingIds = await db.Messages.Where(m => ids.Contains(m.TransactionId) &&
                (m.Transaction.Block.Height < height.Value ||
                 (m.Transaction.Block.Height == height.Value && m.Transaction.Position < position)))
                .Select(m => m.TransactionId).ToListAsync(cancellationToken);
            var allowed = precedingIds.ToHashSet();
            additions = additions.Where(c => allowed.Contains(c.MessageId)).ToList();
        }
        publications.AddRange(additions.Select(c => new Publication(c.MessageId, c.Index, c.Count)));
        if (publications.Count == 0) return null;

        var count = publications[0].Count;
        if (count is < 1 or > ushort.MaxValue || publications.Any(c => c.Count != count || c.Index < 0 || c.Index >= count))
            throw new FormatException("inconsistent attachment chunk records");
        var selected = publications.DistinctBy(c => c.Index).ToArray();
        if (selected.Length != count) return null;

        var assembly = new AttachmentAssembly(attachment.Hash, checked((uint)attachment.TotalLength), attachment.MerkleRoot);
        void Add(Publication publication, byte[] bytes)
        {
            var chunk = DataChunkReader.Read(XtopMessageReader.ReadMessage(bytes, attachment.Network));
            if (publication.Index != chunk.Index || publication.Count != chunk.Count)
                throw new FormatException("chunk record does not match its message");
            assembly.Add(chunk);
        }

        var localIds = additions.Select(c => c.MessageId).ToHashSet();
        var storedIds = selected.Where(c => !localIds.Contains(c.MessageId)).Select(c => c.MessageId).ToArray();
        if (storedIds.Length > 0)
        {
            await foreach (var chunk in query.Where(c => storedIds.Contains(c.MessageId))
                .Select(c => new { c.MessageId, c.Index, c.Count, c.Message.Data })
                .AsAsyncEnumerable().WithCancellation(cancellationToken))
            {
                Add(new Publication(chunk.MessageId, chunk.Index, chunk.Count), chunk.Data);
            }
        }
        var selectedIds = selected.Select(c => c.MessageId).ToHashSet();
        foreach (var chunk in additions.Where(c => selectedIds.Contains(c.MessageId)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Add(new Publication(chunk.MessageId, chunk.Index, chunk.Count), chunk.Message.Data);
        }

        var data = assembly.Build(cancellationToken);
        return data == null ? null : XtopMessageReader.ReadAttachment(data, attachment.Network);
    }

    private sealed record Publication(long MessageId, int Index, int Count);
}
