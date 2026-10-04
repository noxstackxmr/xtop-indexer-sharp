using System.Data;
using System.Globalization;
using IndexerCore.Models.Collections;
using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Monero;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IndexerCore.Services.Collections;

public sealed class CollectionQueryService(IndexerDbContext db, CollectionTermsService termsService,
    IOptions<MoneroOptions> options)
{
    public async Task<CollectionListResponse> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 100);
        var offset = checked((page - 1) * pageSize);
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var tip = await ReadTipAsync(cancellationToken);
        var query = db.Collections.AsNoTracking().Where(c => c.Network == options.Value.XtopNetwork);
        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderByDescending(c => c.CreationMessage.Transaction.Block.Height)
            .ThenByDescending(c => c.CreationMessage.Transaction.Position)
            .Skip(offset).Take(pageSize)
            .Select(c => new
            {
                c.ProtocolId, c.Name, c.MaxSupply, c.MetadataMode, c.PrimaryPrice, c.SaleStartUtc, c.RoyaltyBps,
                Height = c.CreationMessage.Transaction.Block.Height,
                Hash = c.CreationMessage.Transaction.Block.Hash,
                c.CreationMessage.Transaction.Position,
                c.CreationMessage.Transaction.Block.Timestamp
            }).ToListAsync(cancellationToken);
        var items = rows.Select(c => new CollectionSummaryResponse(Hex(c.ProtocolId), c.Name, c.MaxSupply,
            MetadataMode(c.MetadataMode), Atomic(c.PrimaryPrice), c.SaleStartUtc.ToUniversalTime(), c.RoyaltyBps,
            new CollectionCreationResponse(Hex(c.ProtocolId), c.Height, Hex(c.Hash), c.Position, c.Timestamp.ToUniversalTime()))).ToArray();
        await snapshot.CommitAsync(cancellationToken);
        return new CollectionListResponse(options.Value.Network, options.Value.XtopNetwork, tip, page, pageSize, total, items);
    }

    public async Task<CollectionDetailsResponse?> GetAsync(byte[] id, CancellationToken cancellationToken)
    {
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var tip = await ReadTipAsync(cancellationToken);
        var collection = await db.Collections.AsNoTracking()
            .Include(c => c.CreationMessage).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
            .Include(c => c.TermsAttachment).Include(c => c.LocationsAttachment).Include(c => c.Outputs)
            .SingleOrDefaultAsync(c => c.Network == options.Value.XtopNetwork && c.ProtocolId == id, cancellationToken);
        if (collection == null) return null;
        var transaction = collection.CreationMessage.Transaction;
        var block = transaction.Block;
        var resolved = await termsService.ReadBeforeAsync(collection.TermsAttachment, block.Height, transaction.Position,
            collection.ConfigHash, cancellationToken)
            ?? throw new InvalidDataException("stored collection dependencies are incomplete");
        if (resolved.LocationsAttachment.Id != collection.LocationsAttachmentId)
            throw new InvalidDataException("stored collection locations do not match its terms");
        var result = new CollectionDetailsResponse(Hex(collection.ProtocolId), options.Value.Network, collection.Network, tip,
            collection.CreationMessage.Version, Hex(collection.ConfigHash), collection.Name, collection.MaxSupply,
            MetadataMode(collection.MetadataMode), collection.ManagerPermissions,
            new PrimarySaleTermsResponse(Atomic(collection.PrimaryPrice), collection.SaleStartUtc.ToUniversalTime(), Payout(collection.PrimaryPayout)),
            new RoyaltyTermsResponse(collection.RoyaltyBps, Payout(collection.RoyaltyPayout)),
            new CollectionCreationResponse(Hex(transaction.Hash), block.Height, Hex(block.Hash), transaction.Position, block.Timestamp.ToUniversalTime()),
            Reference(collection.TermsAttachment), Reference(collection.LocationsAttachment),
            resolved.Locations.Select(location => new MediaLocationResponse(location.Role, location.Role switch
            {
                1 => "collection_metadata", 2 => "items_metadata", 3 => "placeholder",
                _ => throw new InvalidDataException("invalid stored location role")
            }, location.Uri)).ToArray(),
            collection.Outputs.OrderBy(o => o.Kind).Select(output => new CreationOutputResponse(output.Kind switch
            {
                CollectionOutputKind.Control => "control", CollectionOutputKind.Issuance => "issuance", CollectionOutputKind.Nft => "nft",
                _ => throw new InvalidDataException("invalid stored output kind")
            }, output.OutputIndex, Hex(output.PublicKey), Hex(output.KeyImage), Hex(output.OwnerKey), Atomic(output.NominalAmount),
                output.RangeStart, output.RangeEnd)).ToArray());
        await snapshot.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<ScannedTipResponse?> ReadTipAsync(CancellationToken cancellationToken)
    {
        var tip = await db.Blocks.AsNoTracking().Where(b => b.Network == options.Value.XtopNetwork)
            .OrderByDescending(b => b.Height).Select(b => new { b.Height, b.Hash }).FirstOrDefaultAsync(cancellationToken);
        return tip == null ? null : new ScannedTipResponse(tip.Height, Hex(tip.Hash));
    }

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
    private static string Atomic(decimal amount) => amount.ToString("0", CultureInfo.InvariantCulture);
    private static string MetadataMode(byte mode) => mode switch
    {
        0 => "open", 1 => "delayed", _ => throw new InvalidDataException("invalid stored metadata mode")
    };
    private static PayoutResponse Payout(byte[] keys) => new(Hex(keys[..32]), Hex(keys[32..]));
    private static AttachmentReferenceResponse Reference(Attachment attachment)
        => new(Hex(attachment.Hash), attachment.TotalLength, Hex(attachment.MerkleRoot));
}
