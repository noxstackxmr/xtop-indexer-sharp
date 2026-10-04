namespace IndexerCore.Data.Entities;

public sealed class PrimaryPurchaseItem
{
    public long CollectionId { get; set; }
    public long Serial { get; set; }
    public required byte[] ItemId { get; set; }
    public long PurchaseMessageId { get; set; }
    public long PreviousOutputId { get; set; }
    public long BuyerOutputId { get; set; }
    public PrimaryPurchase Purchase { get; set; } = null!;
    public CollectionOutput PreviousOutput { get; set; } = null!;
    public CollectionOutput BuyerOutput { get; set; } = null!;
}
