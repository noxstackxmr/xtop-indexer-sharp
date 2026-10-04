using IndexerCore.Data.Entities;
using IndexerCore.Monero;

namespace IndexerCore.Services.Indexing;

public sealed class NativeTransactionReader(MoneroRpcClient rpc)
{
    public async Task<MoneroProofTransaction> ReadAsync(Message message, CancellationToken cancellationToken)
    {
        var transaction = message.Transaction;
        var block = transaction.Block;
        if (transaction.NativeData == null)
        {
            var remote = await rpc.GetBlockAsync((ulong)block.Height, cancellationToken);
            var id = Convert.ToHexStringLower(transaction.Hash);
            if (!Convert.FromHexString(remote.Hash).AsSpan().SequenceEqual(block.Hash) || transaction.Position < 0 ||
                transaction.Position >= remote.TransactionIds.Length || remote.TransactionIds[transaction.Position] != id)
                throw new InvalidDataException("block changed before native data could be read");
            var native = (await rpc.GetTransactionsAsync(remote with { TransactionIds = [id] }, cancellationToken))[0];
            if ((await rpc.GetBlockAsync(remote.Height, cancellationToken)).Hash != remote.Hash)
                throw new InvalidDataException("block changed while reading native data");
            transaction.NativeData = native.NativeData;
        }
        var result = MoneroProofTransaction.Parse(transaction.NativeData);
        if (!result.Message.AsSpan().SequenceEqual(message.Data)) throw new InvalidDataException("saved message differs from native transaction");
        return result;
    }
}
