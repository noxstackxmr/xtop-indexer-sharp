using IndexerCore.Models.Collections;

namespace IndexerCore.Models.Transactions;

public sealed record IndexedTransactionResponse(string Id, long BlockHeight, string BlockHash,
    byte? Operation, string Status, string? Error);

public sealed record TransactionStatusResponse(string Network, byte NetworkId, ScannedTipResponse? ScannedTip,
    ScannedTipResponse? SpendCheckedTip, IndexedTransactionResponse? Transaction);
