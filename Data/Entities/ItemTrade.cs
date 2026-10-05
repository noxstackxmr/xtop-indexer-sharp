namespace IndexerCore.Data.Entities;

public sealed class ItemTrade
{
    public long MessageId { get; set; }
    public byte Operation { get; set; }
    public long PreviousOutputId { get; set; }
    public long SuccessorOutputId { get; set; }
    public long? ListingId { get; set; }
    public byte[]? MarketplaceId { get; set; }
    public byte[]? MarketplaceConfigHash { get; set; }
    public byte[]? MarketplacePolicy { get; set; }
    public decimal? Price { get; set; }
    public byte[]? SellerPayout { get; set; }
    public byte[]? ReturnAddress { get; set; }
    public byte[]? ServiceAddress { get; set; }
    public int? FeeBps { get; set; }
    public decimal? SellerAmount { get; set; }
    public decimal? RoyaltyAmount { get; set; }
    public decimal? PlatformFee { get; set; }
    public Message Message { get; set; } = null!;
    public CollectionOutput PreviousOutput { get; set; } = null!;
    public CollectionOutput SuccessorOutput { get; set; } = null!;
    public ItemTrade? Listing { get; set; }
}
