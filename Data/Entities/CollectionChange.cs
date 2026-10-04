namespace IndexerCore.Data.Entities;

public sealed class CollectionChange
{
    public long MessageId { get; set; }
    public long CollectionId { get; set; }
    public byte Network { get; set; }
    public byte Operation { get; set; }
    public required byte[] PreviousKeyImage { get; set; }
    public byte OutputIndex { get; set; }
    public required byte[] PublicKey { get; set; }
    public required byte[] KeyImage { get; set; }
    public required byte[] OwnerKey { get; set; }
    public decimal NominalAmount { get; set; }
    public long? LocationsAttachmentId { get; set; }
    public bool Revealed { get; set; }
    public required string CollectionMetadataUri { get; set; }
    public string? ItemsMetadataUri { get; set; }
    public string? PlaceholderUri { get; set; }

    public Message Message { get; set; } = null!;
    public Collection Collection { get; set; } = null!;
    public Attachment? LocationsAttachment { get; set; }
}
