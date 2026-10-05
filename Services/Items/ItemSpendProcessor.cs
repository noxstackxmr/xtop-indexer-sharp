using System.Linq.Expressions;
using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Monero;
using IndexerCore.Services.Indexing;
using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Services.Items;

public sealed class ItemSpendProcessor(IndexerDbContext db, MoneroRpcClient rpc)
{
    public const int BatchSize = 10;

    public async Task<int> ProcessAsync(byte network, CancellationToken cancellationToken)
    {
        var processed = 0;
        while (processed < BatchSize)
        {
            var block = await db.Blocks.Where(b => b.Network == network && !b.IsProcessed)
                .OrderBy(b => b.Height).FirstOrDefaultAsync(cancellationToken);
            if (block == null || await MessageBatchProcessor.PendingMessages(db, network)
                    .AnyAsync(m => m.Transaction.Block.Height <= block.Height, cancellationToken)) break;

            var remote = await rpc.GetBlockAsync(checked((ulong)block.Height), cancellationToken);
            if (!Convert.FromHexString(remote.Hash).AsSpan().SequenceEqual(block.Hash))
                throw new InvalidDataException("block changed before item spend processing");
            var transactions = await rpc.GetTransactionsAsync(remote, cancellationToken);
            if ((await rpc.GetBlockAsync(remote.Height, cancellationToken)).Hash != remote.Hash)
                throw new InvalidDataException("block changed while reading item spends");

            await using var write = await db.Database.BeginTransactionAsync(cancellationToken);
            for (var position = 0; position < transactions.Length; position++)
                await RecordAsync(block, position, transactions[position], cancellationToken);
            block.IsProcessed = true;
            await db.SaveChangesAsync(cancellationToken);
            await write.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            processed++;
        }
        return processed;
    }

    private async Task RecordAsync(Block block, int position, MoneroTransaction native, CancellationToken cancellationToken)
    {
        var images = MoneroInputReader.ReadKeyImages(native.NativeData);
        if (images.Select(Convert.ToHexString).Distinct().Count() != images.Length)
            throw new InvalidDataException("duplicate native input key image");
        var hash = Convert.FromHexString(native.Id);
        Transaction? stored = null;
        foreach (var batch in images.Chunk(128))
        {
            var outputs = await db.CollectionOutputs.Where(o => o.Network == block.Network && o.Kind == CollectionOutputKind.Item)
                .Where(Matches(batch))
                .Include(o => o.SourceMessage).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
                .Include(o => o.Purchase).ThenInclude(i => i!.Purchase).ThenInclude(p => p.Message).ThenInclude(m => m.Transaction)
                .Include(o => o.Trade).ThenInclude(t => t!.Message).ThenInclude(m => m.Transaction)
                .Include(o => o.Burn).ThenInclude(b => b!.Transaction)
                .ToArrayAsync(cancellationToken);
            foreach (var output in outputs)
            {
                var origin = output.SourceMessage.Transaction;
                if (origin.Block.Height > block.Height || (origin.Block.Height == block.Height && origin.Position >= position))
                    throw new InvalidDataException("item spend precedes its output");
                if (output.Purchase != null || output.Trade != null)
                {
                    var purchase = output.Purchase?.Purchase.Message.Transaction ?? output.Trade!.Message.Transaction;
                    if (purchase.BlockId != block.Id || purchase.Position != position || !purchase.Hash.AsSpan().SequenceEqual(hash))
                        throw new InvalidDataException("item input conflicts with its indexed transition");
                    if (output.Burn != null) db.ItemBurns.Remove(output.Burn);
                    continue;
                }
                if (output.Burn != null)
                {
                    var previous = output.Burn.Transaction;
                    if (previous.BlockId != block.Id || previous.Position != position || !previous.Hash.AsSpan().SequenceEqual(hash))
                        throw new InvalidDataException("item input has already been spent");
                    continue;
                }
                if (stored == null)
                {
                    stored = await db.Transactions.SingleOrDefaultAsync(t => t.BlockId == block.Id && t.Position == position, cancellationToken);
                    if (stored == null)
                    {
                        stored = new Transaction { Block = block, Hash = hash, Position = position, NativeData = native.NativeData };
                        db.Transactions.Add(stored);
                    }
                    else if (!stored.Hash.AsSpan().SequenceEqual(hash)) throw new InvalidDataException("stored transaction differs from block");
                    stored.NativeData ??= native.NativeData;
                }
                db.ItemBurns.Add(new ItemBurn { Output = output, Transaction = stored });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Expression<Func<CollectionOutput, bool>> Matches(byte[][] images)
    {
        var output = Expression.Parameter(typeof(CollectionOutput), "output");
        var key = Expression.Property(output, nameof(CollectionOutput.KeyImage));
        Expression match = Expression.Constant(false);
        foreach (var image in images) match = Expression.OrElse(match, Expression.Equal(key, Expression.Constant(image)));
        return Expression.Lambda<Func<CollectionOutput, bool>>(match, output);
    }
}
