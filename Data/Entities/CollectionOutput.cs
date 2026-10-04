namespace IndexerCore.Data.Entities;

public enum CollectionOutputKind : byte
{
    Control,
    Issuance,
    Item
}

public sealed class CollectionOutput
{
    public long Id { get; set; }
    public long CollectionId { get; set; }
    public long SourceMessageId { get; set; }
    public byte Network { get; set; }
    public CollectionOutputKind Kind { get; set; }
    public byte[]? ItemId { get; set; }
    public byte OutputIndex { get; set; }
    public required byte[] PublicKey { get; set; }
    public required byte[] KeyImage { get; set; }
    public required byte[] OwnerKey { get; set; }
    public decimal NominalAmount { get; set; }
    public long? RangeStart { get; set; }
    public long? RangeEnd { get; set; }
    public Collection Collection { get; set; } = null!;
    public Message SourceMessage { get; set; } = null!;
    public IssuanceSplit? Split { get; set; }
    public PrimaryPurchaseItem? Purchase { get; set; }
    public PrimaryPurchaseItem? PurchaseOrigin { get; set; }
}
