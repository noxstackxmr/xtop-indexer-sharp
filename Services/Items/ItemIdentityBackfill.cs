using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Protocol.Items;
using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Services.Items;

public sealed class ItemIdentityBackfill(IndexerDbContext db)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var rows = await db.CollectionOutputs.Where(o => o.Kind == CollectionOutputKind.Item && o.ItemId == null)
                .OrderBy(o => o.Id).Take(500).Select(o => new { o.Id, o.Collection.ProtocolId, o.RangeStart }).ToArrayAsync(cancellationToken);
            if (rows.Length == 0) return;
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            foreach (var row in rows)
            {
                if (row.RangeStart == null) throw new InvalidDataException("item output has no serial");
                var id = ItemIdentity.Derive(row.ProtocolId, checked((uint)row.RangeStart.Value));
                await db.CollectionOutputs.Where(o => o.Id == row.Id && o.ItemId == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(o => o.ItemId, id), cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
