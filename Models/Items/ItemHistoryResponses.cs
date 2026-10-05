using IndexerCore.Models.Collections;

namespace IndexerCore.Models.Items;

public sealed record ItemHistoryPaymentsResponse(string SellerAmountAtomic, string RoyaltyAmountAtomic,
    int PlatformFeeBps, string PlatformFeeAtomic, string TotalPriceAtomic);

public sealed record ItemHistoryEventResponse(string Type, CollectionCreationResponse Transaction,
    string? FromOwnerKey, string? ToOwnerKey, string? ServiceOwnerKey, string? ListingId,
    ItemOutputResponse? PreviousOutput, ItemOutputResponse? Output, string? PriceAtomic,
    ItemHistoryPaymentsResponse? Payments, string? Mode = null, string? SignerOwnerKey = null);

public sealed record ItemHistoryResponse(string Network, byte NetworkId, ScannedTipResponse? ScannedTip,
    ScannedTipResponse? SpendCheckedTip, string ItemId, string CollectionId, long Serial,
    int Page, int PageSize, long Total, ItemHistoryEventResponse[] Events);
