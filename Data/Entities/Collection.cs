namespace IndexerCore.Data.Entities;

public sealed class Collection
{
    public long Id { get; set; }
    public byte Network { get; set; }
    public byte[] ProtocolId { get; set; } = [];
    public byte[] ConfigHash { get; set; } = [];
    public long CreationMessageId { get; set; }
    public long TermsAttachmentId { get; set; }
    public long LocationsAttachmentId { get; set; }
    public required string Name { get; set; }
    public long MaxSupply { get; set; }
    public byte MetadataMode { get; set; }
    public byte ManagerPermissions { get; set; }
    public required byte[] PrimaryPayout { get; set; }
    public required byte[] RoyaltyPayout { get; set; }
    public int RoyaltyBps { get; set; }
    public decimal PrimaryPrice { get; set; }
    public DateTimeOffset SaleStartUtc { get; set; }

    public Message CreationMessage { get; set; } = null!;
    public Attachment TermsAttachment { get; set; } = null!;
    public Attachment LocationsAttachment { get; set; } = null!;
    public List<CollectionOutput> Outputs { get; set; } = [];
}
