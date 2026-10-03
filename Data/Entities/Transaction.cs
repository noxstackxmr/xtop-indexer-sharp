namespace IndexerCore.Data.Entities;

public sealed class Transaction
{
    public long Id { get; set; }
    public long BlockId { get; set; }
    public required byte[] Hash { get; set; }
    public int Position { get; set; }
    public byte[]? NativeData { get; set; }
    public Block Block { get; set; } = null!;
    public Message? Message { get; set; }
}
