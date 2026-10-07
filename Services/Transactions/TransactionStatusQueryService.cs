using System.Data;
using IndexerCore.Data;
using IndexerCore.Models.Transactions;
using IndexerCore.Monero;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static IndexerCore.Services.Items.ItemReadData;

namespace IndexerCore.Services.Transactions;

public sealed class TransactionStatusQueryService(IndexerDbContext db, IOptions<MoneroOptions> options)
{
    public async Task<TransactionStatusResponse> GetAsync(byte[] hash, CancellationToken cancellationToken)
    {
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var tip = await Tip(db, options.Value.XtopNetwork, false, cancellationToken);
        var checkedTip = await Tip(db, options.Value.XtopNetwork, true, cancellationToken);
        var tx = await db.Transactions.AsNoTracking().Include(t => t.Block).Include(t => t.Message)
            .SingleOrDefaultAsync(t => t.Hash == hash && t.Block.Network == options.Value.XtopNetwork, cancellationToken);
        var result = new TransactionStatusResponse(options.Value.Network, options.Value.XtopNetwork, tip, checkedTip,
            tx == null ? null : new(Hex(tx.Hash), tx.Block.Height, Hex(tx.Block.Hash), tx.Message?.Operation,
                tx.Message?.Status.ToString().ToLowerInvariant() ?? "unrecognized", tx.Message?.Error));
        await snapshot.CommitAsync(cancellationToken);
        return result;
    }
}
