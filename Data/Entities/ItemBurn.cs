namespace IndexerCore.Data.Entities;

public sealed class ItemBurn
{
    public long OutputId { get; set; }
    public long TransactionId { get; set; }
    public CollectionOutput Output { get; set; } = null!;
    public Transaction Transaction { get; set; } = null!;
}
