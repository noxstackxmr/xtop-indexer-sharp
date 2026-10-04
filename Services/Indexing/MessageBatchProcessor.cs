using IndexerCore.Protocol.Messages;
using System.Security.Cryptography;
using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Services.Attachments;
using IndexerCore.Services.Collections;
using IndexerCore.Services.Issuance;
using IndexerCore.Services.Sales;
using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Services.Indexing;

public sealed class MessageBatchProcessor(IndexerDbContext db, DataChunkHandler chunks, AttachmentService attachments,
    CollectionCreateHandler collections, CollectionControlHandler controls, IssueSplitHandler splits, PrimaryPurchaseHandler purchases)
{
    public static IQueryable<Message> PendingMessages(IndexerDbContext db, byte network) => db.Messages
        .Where(m => (m.Status == MessageStatus.Pending ||
                     (m.Status == MessageStatus.Parsed && (m.Operation == 0x01 || m.Operation == 0x02 ||
                         m.Operation == 0x0D || m.Operation == 0x0E || m.Operation == 0x0F || m.Operation == 0x12 || m.Operation == 9 || m.Operation == 0x13))) &&
                    m.Transaction.Block.Network == network);

    public async Task<int> ProcessAsync(byte network, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var messages = await PendingMessages(db, network).Include(m => m.Transaction).ThenInclude(t => t.Block)
            .OrderBy(m => m.Transaction.Block.Height).ThenBy(m => m.Transaction.Position)
            .Take(100).ToListAsync(cancellationToken);
        var affected = new HashSet<Attachment>();
        foreach (var message in messages)
        {
            try
            {
                var envelope = XtopMessageReader.ReadMessage(message.Data, network);
                message.Operation = envelope.Operation;
                switch (envelope.Operation)
                {
                    case 0x01:
                        affected.Add(await chunks.HandleAsync(message, envelope, cancellationToken));
                        message.Status = MessageStatus.Valid;
                        break;
                    case 0x02:
                        await collections.HandleAsync(message, envelope, cancellationToken);
                        message.Status = MessageStatus.Valid;
                        break;
                    case 0x0D:
                    case 0x0E:
                    case 0x0F:
                        await controls.HandleAsync(message, envelope, cancellationToken);
                        message.Status = MessageStatus.Valid;
                        break;
                    case 0x12:
                        await splits.HandleAsync(message, envelope, cancellationToken);
                        message.Status = MessageStatus.Valid;
                        break;
                    case 9:
                    case 0x13:
                        await purchases.HandleAsync(message, envelope, cancellationToken);
                        message.Status = MessageStatus.Valid;
                        break;
                    default:
                        message.Status = MessageStatus.Parsed;
                        break;
                }
                if (message.Status == MessageStatus.Valid && message.Transaction.Block.IsProcessed)
                {
                    await db.Blocks.Where(b => b.Network == network && b.Height >= message.Transaction.Block.Height)
                        .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsProcessed, false), cancellationToken);
                    message.Transaction.Block.IsProcessed = false;
                }
                message.Error = null;
            }
            catch (NotSupportedException exception)
            {
                message.Status = MessageStatus.Unsupported;
                message.Error = exception.Message;
            }
            catch (Exception exception) when (exception is FormatException or CryptographicException or OverflowException)
            {
                message.Status = MessageStatus.Invalid;
                message.Error = exception.Message;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        var ready = await attachments.GetReadyAsync(network, cancellationToken);
        affected.UnionWith(ready);
        foreach (var attachment in affected.Where(a => a.Status == AttachmentStatus.Incomplete))
            await attachments.RefreshAsync(attachment, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Math.Max(messages.Count, ready.Count);
    }
}
