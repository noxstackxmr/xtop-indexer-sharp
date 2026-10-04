namespace IndexerCore.Data.Entities;

public sealed class PrimaryPurchase
{
    public long MessageId { get; set; }
    public long CollectionId { get; set; }
    public int Profile { get; set; }
    public int FeeBps { get; set; }
    public decimal CreatorAmount { get; set; }
    public decimal PlatformFee { get; set; }
    public Message Message { get; set; } = null!;
    public Collection Collection { get; set; } = null!;
    public List<PrimaryPurchaseItem> Items { get; set; } = [];
}
