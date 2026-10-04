namespace IndexerCore.Data.Entities;

public sealed class IssuanceSplit
{
    public long MessageId { get; set; }
    public long ParentOutputId { get; set; }
    public Message Message { get; set; } = null!;
    public CollectionOutput ParentOutput { get; set; } = null!;
}
