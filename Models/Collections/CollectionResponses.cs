namespace IndexerCore.Models.Collections;

public sealed record ScannedTipResponse(long Height, string Hash);

public sealed record CollectionCreationResponse(string TransactionId, long BlockHeight, string BlockHash,
    int TransactionPosition, DateTimeOffset BlockTimeUtc);

public sealed record CollectionSummaryResponse(string Id, string Name, long MaxSupply, string MetadataMode,
    string MetadataState, string PrimaryPriceAtomic, DateTimeOffset SaleStartUtc, int RoyaltyBps, CollectionCreationResponse Creation);

public sealed record CollectionListResponse(string Network, byte NetworkId, ScannedTipResponse? ScannedTip,
    int Page, int PageSize, long Total, CollectionSummaryResponse[] Items);

public sealed record PayoutResponse(string PublicSpendKey, string PublicViewKey);

public sealed record PrimarySaleTermsResponse(string PriceAtomic, DateTimeOffset StartUtc, PayoutResponse Payout);

public sealed record RoyaltyTermsResponse(int BasisPoints, PayoutResponse Payout);

public sealed record AttachmentReferenceResponse(string Hash, long TotalLength, string MerkleRoot);

public sealed record MediaLocationResponse(byte Role, string Type, string Uri);

public sealed record CreationOutputResponse(string Kind, byte OutputIndex, string PublicKey, string KeyImage,
    string OwnerKey, string NominalAmountAtomic, long? RangeStart, long? RangeEnd);

public sealed record CurrentControlResponse(string TransactionId, byte OutputIndex, string PublicKey, string KeyImage,
    string OwnerKey, string NominalAmountAtomic);

public sealed record CollectionChangeResponse(string Operation, CollectionCreationResponse Transaction,
    AttachmentReferenceResponse? LocationsAttachment);

public sealed record CollectionDetailsResponse(string Id, string Network, byte NetworkId, ScannedTipResponse? ScannedTip,
    byte ProtocolVersion, string ConfigHash, string Name, long MaxSupply, string MetadataMode, string MetadataState, byte ManagerPermissions,
    PrimarySaleTermsResponse PrimarySale, RoyaltyTermsResponse Royalty, CollectionCreationResponse Creation,
    AttachmentReferenceResponse TermsAttachment, AttachmentReferenceResponse LocationsAttachment,
    MediaLocationResponse[] Locations, CreationOutputResponse[] CreationOutputs,
    CurrentControlResponse CurrentControl, CollectionChangeResponse? LastChange);
