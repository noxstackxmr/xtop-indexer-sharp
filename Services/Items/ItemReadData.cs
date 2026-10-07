using System.Globalization;
using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Models.Collections;
using IndexerCore.Models.Items;
using Microsoft.EntityFrameworkCore;
using IndexerCore.Protocol.Collections;
using IndexerCore.Protocol.Issuance;
using IndexerCore.Protocol.Messages;

namespace IndexerCore.Services.Items;

internal static class ItemReadData
{
    public static string Hex(byte[] value) => Convert.ToHexStringLower(value);
    public static string Atomic(decimal value) => value.ToString("0", CultureInfo.InvariantCulture);
    public static PayoutResponse Payout(byte[] value) => new(Hex(value[..32]), Hex(value[32..]));
    public static CollectionCreationResponse Transaction(Transaction tx) => new(Hex(tx.Hash), tx.Block.Height,
        Hex(tx.Block.Hash), tx.Position, tx.Block.Timestamp.ToUniversalTime());
    public static ItemOutputResponse Output(CollectionOutput output)
    {
        var tx = output.SourceMessage.Transaction;
        var witnesses = Witnesses(output);
        return new(Hex(tx.Hash), output.OutputIndex, Hex(output.PublicKey), Hex(output.KeyImage), Atomic(output.NominalAmount),
            tx.Block.Height, Hex(tx.Block.Hash), tx.Block.Timestamp.ToUniversalTime(), witnesses.Owner, witnesses.Amount);
    }
    public static (byte Owner, byte Amount) Witnesses(CollectionOutput output)
    {
        if (output.SourceMessage.Operation == 9) return (0, 1);
        if (output.SourceMessage.Operation is not (2 or 0x12)) return (0, 0);
        var message = XtopMessageReader.ReadMessage(output.SourceMessage.Data, output.Network);
        if (message.Operation == 0x12 && IssueSplitReader.ReadProfile(message) == CompactIssueSplitProofs.Profile) return (0, 0);
        var binding = message.Operation == 2 ? CollectionCreateReader.Read(message).IssuanceRoot :
            IssueSplitReader.Read(message).Children.Single(c => c.Binding.OutputIndex == output.OutputIndex).Binding;
        return (binding.OwnershipWitness, binding.AmountWitness);
    }
    public static async Task<ScannedTipResponse?> Tip(IndexerDbContext db, byte network, bool processed, CancellationToken cancellationToken)
    {
        var tip = await db.Blocks.AsNoTracking().Where(b => b.Network == network && (!processed || b.IsProcessed))
            .OrderByDescending(b => b.Height).Select(b => new { b.Height, b.Hash }).FirstOrDefaultAsync(cancellationToken);
        return tip == null ? null : new(tip.Height, Hex(tip.Hash));
    }
    public static int Offset(int page, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 100);
        return checked((page - 1) * pageSize);
    }
}
