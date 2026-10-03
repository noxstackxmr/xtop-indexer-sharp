using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Protocol;
using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Services;

public sealed class DataChunkHandler(IndexerDbContext db)
{
    public async Task<Attachment> HandleAsync(Message message, XtopMessage envelope, CancellationToken cancellationToken)
    {
        var chunk = DataChunkReader.Read(envelope);
        if (!DataChunkMerkle.Verify(chunk))
            throw new FormatException("invalid chunk Merkle proof");
        var existing = db.DataChunks.Local.FirstOrDefault(c => c.MessageId == message.TransactionId)?.Attachment;
        existing ??= await db.DataChunks.Where(c => c.MessageId == message.TransactionId)
            .Select(c => c.Attachment).SingleOrDefaultAsync(cancellationToken);
        if (existing != null) return existing;

        var attachment = db.Attachments.Local.FirstOrDefault(a =>
            a.Network == message.Network && a.TotalLength == chunk.TotalLength &&
            a.Hash.AsSpan().SequenceEqual(chunk.Hash) && a.MerkleRoot.AsSpan().SequenceEqual(chunk.MerkleRoot));
        attachment ??= await db.Attachments.SingleOrDefaultAsync(a =>
            a.Network == message.Network && a.TotalLength == chunk.TotalLength &&
            a.Hash == chunk.Hash && a.MerkleRoot == chunk.MerkleRoot, cancellationToken);

        if (attachment == null)
        {
            attachment = new Attachment
            {
                Network = message.Network,
                Hash = chunk.Hash,
                TotalLength = chunk.TotalLength,
                MerkleRoot = chunk.MerkleRoot
            };
            db.Attachments.Add(attachment);
        }

        db.DataChunks.Add(new DataChunk
        {
            MessageId = message.TransactionId,
            Message = message,
            Attachment = attachment,
            Index = chunk.Index,
            Count = chunk.Count
        });
        return attachment;
    }
}
