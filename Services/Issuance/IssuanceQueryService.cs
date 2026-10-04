using IndexerCore.Data;
using IndexerCore.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Services.Issuance;

public sealed record PreparedOutput(long FirstSerial, long Count, string State, byte[] TransactionId, byte OutputIndex,
    byte[] PublicKey, byte[] KeyImage, byte[] OwnerKey, decimal NominalAmount);

public sealed class IssuanceQueryService(IndexerDbContext db)
{
    public async Task<PreparedOutput[]> ReadAsync(byte network, byte[] collectionId, CancellationToken cancellationToken)
    {
        var outputs = await db.CollectionOutputs.AsNoTracking()
            .Where(o => o.Network == network && o.Collection.ProtocolId == collectionId &&
                o.Kind != CollectionOutputKind.Control && o.Split == null && o.Purchase == null && o.PurchaseOrigin == null)
            .OrderBy(o => o.RangeStart)
            .Select(o => new { o.RangeStart, o.RangeEnd, o.Kind, o.SourceMessage.Transaction.Hash, o.OutputIndex,
                o.PublicKey, o.KeyImage, o.OwnerKey, o.NominalAmount }).ToListAsync(cancellationToken);
        return outputs.Select(o => new PreparedOutput(o.RangeStart!.Value, o.RangeEnd!.Value - o.RangeStart.Value,
            o.Kind == CollectionOutputKind.Nft ? "prepared_unsold" : "issuance", o.Hash, o.OutputIndex,
            o.PublicKey, o.KeyImage, o.OwnerKey, o.NominalAmount)).ToArray();
    }
}
