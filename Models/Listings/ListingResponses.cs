using IndexerCore.Models.Collections;
using IndexerCore.Models.Items;

namespace IndexerCore.Models.Listings;

public sealed record ListingResponse(string Id, string ItemId, string CollectionId, long Serial, string Status,
    string PriceAtomic, int PlatformFeeBps, string PlatformFeeAtomic, string TotalPriceAtomic,
    string SellerAmountAtomic, int RoyaltyBps, string RoyaltyAmountAtomic,
    string SellerOwnerKey, string ServiceOwnerKey, PayoutResponse SellerPayout, PayoutResponse ReturnAddress,
    PayoutResponse ServiceAddress, PayoutResponse RoyaltyPayout, PayoutResponse PlatformPayout,
    ItemOutputResponse Output, long Confirmations, long BlocksUntilUnlock, bool IsUnlocked,
    CollectionCreationResponse? Resolution);

public sealed record ListingListResponse(string Network, byte NetworkId, ScannedTipResponse? ScannedTip,
    ScannedTipResponse? SpendCheckedTip, int Page, int PageSize, long Total, ListingResponse[] Listings);

public sealed record ListingDetailsResponse(string Network, byte NetworkId, ScannedTipResponse? ScannedTip,
    ScannedTipResponse? SpendCheckedTip, ListingResponse Listing);
