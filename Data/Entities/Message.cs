namespace IndexerCore.Data.Entities;

public enum MessageStatus : byte
{
    Pending,
    Valid,
    Invalid,
    Unsupported,
    Parsed
}

public sealed class Message
{
    public long TransactionId { get; set; }
    public byte Version { get; set; }
    public byte Network { get; set; }
    public byte Operation { get; set; }
    public required byte[] Data { get; set; }
    public MessageStatus Status { get; set; } = MessageStatus.Pending;
    public string? Error { get; set; }

    public Transaction Transaction { get; set; } = null!;
}
