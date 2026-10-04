using System.Data;
using System.Globalization;
using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Models.Collections;
using IndexerCore.Models.Items;
using IndexerCore.Monero;
using IndexerCore.Services.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IndexerCore.Services.Items;

public sealed class ItemQueryService(IndexerDbContext db, CollectionStateService states, IOptions<MoneroOptions> options)
{
    public async Task<ItemListResponse> ListAsync(byte[]? collectionId, string? status, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 100);
        if (collectionId is { Length: not 32 }) throw new ArgumentException("invalid collection id", nameof(collectionId));
        if (status is not (null or "prepared_unsold" or "sold")) throw new ArgumentException("invalid item status", nameof(status));
        var offset = checked((page - 1) * pageSize);
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var tip = await ReadTipAsync(cancellationToken);
        var query = CurrentOutputs();
        if (collectionId != null) query = query.Where(o => o.Collection.ProtocolId == collectionId);
        if (status == "prepared_unsold") query = query.Where(o => o.PurchaseOrigin == null);
        if (status == "sold") query = query.Where(o => o.PurchaseOrigin != null);
        var total = await query.LongCountAsync(cancellationToken);
        var rows = await WithDetails(query).OrderByDescending(o => o.Collection.CreationMessage.Transaction.Block.Height)
            .ThenByDescending(o => o.Collection.CreationMessage.Transaction.Position).ThenBy(o => o.RangeStart)
            .Skip(offset).Take(pageSize).ToArrayAsync(cancellationToken);
        var metadata = await ReadMetadataAsync(rows, cancellationToken);
        var items = rows.Select(o => Response(o, metadata[o.CollectionId])).ToArray();
        await snapshot.CommitAsync(cancellationToken);
        return new(options.Value.Network, options.Value.XtopNetwork, tip, page, pageSize, total, items);
    }

    public async Task<ItemDetailsResponse?> GetAsync(byte[] itemId, CancellationToken cancellationToken)
    {
        if (itemId.Length != 32) throw new ArgumentException("invalid item id", nameof(itemId));
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var tip = await ReadTipAsync(cancellationToken);
        var output = await WithDetails(CurrentOutputs()).SingleOrDefaultAsync(o => o.ItemId == itemId, cancellationToken);
        if (output == null) return null;
        var metadata = await ReadMetadataAsync([output], cancellationToken);
        var result = new ItemDetailsResponse(options.Value.Network, options.Value.XtopNetwork, tip,
            Response(output, metadata[output.CollectionId]));
        await snapshot.CommitAsync(cancellationToken);
        return result;
    }

    private IQueryable<CollectionOutput> CurrentOutputs() => db.CollectionOutputs.AsNoTracking()
        .Where(o => o.Network == options.Value.XtopNetwork && o.Kind == CollectionOutputKind.Item && o.ItemId != null &&
            o.Split == null && o.Purchase == null);

    private static IQueryable<CollectionOutput> WithDetails(IQueryable<CollectionOutput> query) => query
        .Include(o => o.Collection)
        .Include(o => o.SourceMessage).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
        .Include(o => o.PurchaseOrigin).ThenInclude(i => i!.Purchase);

    private async Task<Dictionary<long, ItemMetadataResponse>> ReadMetadataAsync(CollectionOutput[] outputs, CancellationToken cancellationToken)
    {
        var ids = outputs.Select(o => o.CollectionId).Distinct().ToArray();
        if (ids.Length == 0) return [];
        var collections = await db.Collections.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Include(c => c.CreationMessage).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
            .Include(c => c.TermsAttachment).Include(c => c.Outputs.Where(o => o.Kind == CollectionOutputKind.Control))
            .ToArrayAsync(cancellationToken);
        var result = new Dictionary<long, ItemMetadataResponse>();
        foreach (var collection in collections)
        {
            var state = await states.ReadBeforeAsync(collection, long.MaxValue, int.MaxValue, cancellationToken);
            result[collection.Id] = new(state.Metadata.Status,
                state.Metadata.Locations.SingleOrDefault(l => l.Role == 2)?.Uri,
                state.Metadata.Locations.SingleOrDefault(l => l.Role == 3)?.Uri);
        }
        return result;
    }

    private static ItemResponse Response(CollectionOutput output, ItemMetadataResponse metadata)
    {
        var transaction = output.SourceMessage.Transaction;
        var block = transaction.Block;
        var purchase = output.PurchaseOrigin?.Purchase;
        return new(Hex(output.ItemId!), Hex(output.Collection.ProtocolId), output.RangeStart!.Value,
            purchase == null ? "prepared_unsold" : "sold", Hex(output.OwnerKey),
            new(Hex(transaction.Hash), output.OutputIndex, Hex(output.PublicKey), Hex(output.KeyImage), Atomic(output.NominalAmount),
                block.Height, Hex(block.Hash), block.Timestamp.ToUniversalTime()),
            purchase == null ? null : new(Hex(transaction.Hash), block.Height, Hex(block.Hash), block.Timestamp.ToUniversalTime(),
                Atomic(output.Collection.PrimaryPrice), purchase.FeeBps, Atomic(decimal.Floor(output.Collection.PrimaryPrice * purchase.FeeBps / 10000))),
            metadata);
    }

    private async Task<ScannedTipResponse?> ReadTipAsync(CancellationToken cancellationToken)
    {
        var tip = await db.Blocks.AsNoTracking().Where(b => b.Network == options.Value.XtopNetwork)
            .OrderByDescending(b => b.Height).Select(b => new { b.Height, b.Hash }).FirstOrDefaultAsync(cancellationToken);
        return tip == null ? null : new(tip.Height, Hex(tip.Hash));
    }

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
    private static string Atomic(decimal amount) => amount.ToString("0", CultureInfo.InvariantCulture);
}
