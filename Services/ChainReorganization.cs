using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Monero;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IndexerCore.Services;

public sealed class ChainReorganization(
    IndexerDbContext db,
    MoneroRpcClient rpc,
    AttachmentService attachmentService,
    SemaphoreSlim stateLock,
    IOptions<MoneroOptions> options,
    ILogger<ChainReorganization> logger)
{
    public async Task<(Block? LastBlock, ulong NextHeight)> ReconcileAsync(
        MoneroChainInfo info, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (info.Network != settings.Network)
            throw new InvalidDataException($"expected network {settings.Network}, got {info.Network}");

        await stateLock.WaitAsync(cancellationToken);
        try
        {
            var blocks = db.Blocks.AsNoTracking().Where(b => b.Network == settings.XtopNetwork);
            var lastBlock = await blocks.OrderByDescending(b => b.Height).FirstOrDefaultAsync(cancellationToken);
            if (lastBlock == null) return (null, settings.StartHeight);
            if (info.Height <= (ulong)lastBlock.Height)
                throw new InvalidDataException("daemon is behind the stored chain; waiting");

            var anchor = await rpc.GetBlockAsync((ulong)lastBlock.Height, cancellationToken);
            if (Matches(lastBlock.Hash, anchor.Hash))
                return (lastBlock, checked((ulong)lastBlock.Height + 1));

            var current = anchor;
            var ancestor = lastBlock;
            var firstChangedHeight = lastBlock.Height;
            while (!Matches(ancestor.Hash, current.Hash))
            {
                if (ancestor.Height == 0)
                    throw new InvalidDataException("genesis block does not match the stored chain");
                firstChangedHeight = ancestor.Height;
                var previous = await blocks.Where(b => b.Height < ancestor.Height)
                    .OrderByDescending(b => b.Height).FirstOrDefaultAsync(cancellationToken);
                if (previous == null)
                {
                    ancestor = null;
                    break;
                }
                if (previous.Height != ancestor.Height - 1 || !ancestor.PreviousHash.AsSpan().SequenceEqual(previous.Hash))
                    throw new InvalidDataException("stored blocks do not form a continuous chain");

                var remotePrevious = await rpc.GetBlockAsync((ulong)previous.Height, cancellationToken);
                if (!string.Equals(current.PreviousHash, remotePrevious.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("chain changed while searching for the common block; retrying");
                current = remotePrevious;
                ancestor = previous;
            }

            var confirmedAnchor = await rpc.GetBlockAsync(anchor.Height, cancellationToken);
            if (!string.Equals(anchor.Hash, confirmedAnchor.Hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("chain changed before rollback; retrying");

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var attachmentIds = await db.DataChunks
                .Where(c => c.Message.Transaction.Block.Network == settings.XtopNetwork &&
                            c.Message.Transaction.Block.Height >= firstChangedHeight)
                .Select(c => c.AttachmentId).Distinct().ToArrayAsync(cancellationToken);
            var removed = await db.Blocks
                .Where(b => b.Network == settings.XtopNetwork && b.Height >= firstChangedHeight)
                .ExecuteDeleteAsync(cancellationToken);
            var attachments = db.Attachments.Where(a => a.Network == settings.XtopNetwork && attachmentIds.Contains(a.Id));
            await attachments.Where(a => !a.Chunks.Any()).ExecuteDeleteAsync(cancellationToken);
            foreach (var attachment in await attachments.ToListAsync(cancellationToken))
                await attachmentService.RefreshAsync(attachment, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning("rolled back {Count} blocks from height {Height}", removed, firstChangedHeight);
            return (ancestor, checked((ulong)firstChangedHeight));
        }
        finally
        {
            stateLock.Release();
        }
    }

    private static bool Matches(byte[] storedHash, string remoteHash)
        => Convert.FromHexString(remoteHash).AsSpan().SequenceEqual(storedHash);
}
