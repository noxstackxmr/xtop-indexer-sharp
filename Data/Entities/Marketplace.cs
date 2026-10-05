namespace IndexerCore.Data.Entities;

public sealed class Marketplace
{
    public long Id { get; set; }
    public byte Network { get; set; }
    public byte[] ProtocolId { get; set; } = [];
    public byte[] ManagementKey { get; set; } = [];
    public long RegistrationMessageId { get; set; }
    public Message RegistrationMessage { get; set; } = null!;
    public List<MarketplaceRevision> Revisions { get; set; } = [];
}
