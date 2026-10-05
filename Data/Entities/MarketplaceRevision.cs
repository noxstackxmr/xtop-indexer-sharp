namespace IndexerCore.Data.Entities;

public sealed class MarketplaceRevision
{
    public long MessageId { get; set; }
    public long MarketplaceId { get; set; }
    public long Revision { get; set; }
    public byte[] ConfigHash { get; set; } = [];
    public long? PreviousMessageId { get; set; }
    public required string Name { get; set; }
    public required string WebsiteUrl { get; set; }
    public required string CommunicationUrl { get; set; }
    public int ApiVersion { get; set; }
    public byte Modes { get; set; }
    public decimal CreationFee { get; set; }
    public int PrimaryFeeBps { get; set; }
    public int SecondaryFeeBps { get; set; }
    public byte[] FeeAddress { get; set; } = [];
    public byte[] CustodyAddress { get; set; } = [];
    public Message Message { get; set; } = null!;
    public Marketplace Marketplace { get; set; } = null!;
    public MarketplaceRevision? Previous { get; set; }
}
