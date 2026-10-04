using IndexerCore.Models.Collections;

namespace IndexerCore.Models.Items;

public sealed record ItemOutputResponse(string TransactionId, byte OutputIndex, string PublicKey, string KeyImage,
    string NominalAmountAtomic, long BlockHeight, string BlockHash, DateTimeOffset BlockTimeUtc);

public sealed record ItemPurchaseResponse(string TransactionId, long BlockHeight, string BlockHash, DateTimeOffset BlockTimeUtc,
    string PriceAtomic, int PlatformFeeBps, string PlatformFeeAtomic);

public sealed record ItemMetadataResponse(string State, string? ItemsMetadataUri, string? PlaceholderUri);

public sealed record ItemBurnResponse(string Reason, string TransactionId, long BlockHeight, string BlockHash,
    int TransactionPosition, DateTimeOffset BlockTimeUtc);

public sealed record ItemResponse(string ItemId, string CollectionId, long Serial, string Status, string OwnerKey,
    ItemOutputResponse Output, ItemPurchaseResponse? PrimaryPurchase, ItemMetadataResponse Metadata, ItemBurnResponse? Burn);

public sealed record ItemDetailsResponse(string Network, byte NetworkId, ScannedTipResponse? ScannedTip, ItemResponse Item,
    ScannedTipResponse? SpendCheckedTip);

public sealed record ItemListResponse(string Network, byte NetworkId, ScannedTipResponse? ScannedTip,
    int Page, int PageSize, long Total, ItemResponse[] Items, ScannedTipResponse? SpendCheckedTip);
