namespace IndexerCore.Data.Entities;

public sealed class Block
{
    public long Id { get; set; }
    public byte Network { get; set; }
    public long Height { get; set; }
    public required byte[] Hash { get; set; }
    public required byte[] PreviousHash { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public bool IsProcessed { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];
}
