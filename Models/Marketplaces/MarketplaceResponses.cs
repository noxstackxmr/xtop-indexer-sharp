using IndexerCore.Models.Collections;

namespace IndexerCore.Models.Marketplaces;

public sealed record MarketplaceResponse(string Id, string ManagementPublicKey, string ConfigHash, byte FormatVersion,
    long Revision, string? PreviousConfigHash, string Name, string WebsiteUrl, string CommunicationUrl, int ApiVersion,
    string[] SupportedModes, string CreationFeeAtomic, int PrimaryFeeBps, int SecondaryFeeBps,
    PayoutResponse FeeAddress, PayoutResponse? CustodyAddress,
    CollectionCreationResponse Registration, CollectionCreationResponse Publication);

public sealed record MarketplaceListResponse(string Network, byte NetworkId, ScannedTipResponse? ScannedTip,
    int Page, int PageSize, long Total, MarketplaceResponse[] Marketplaces);

public sealed record MarketplaceDetailsResponse(string Network, byte NetworkId, ScannedTipResponse? ScannedTip, MarketplaceResponse Marketplace);
