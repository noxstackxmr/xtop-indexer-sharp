namespace IndexerCore.Data.Entities;

public sealed class DataChunk
{
    public byte[] ConfigHash { get; set; } = [];
    public long MessageId { get; set; }
    public long AttachmentId { get; set; }
    public int Index { get; set; }
    public int Count { get; set; }

    public Message Message { get; set; } = null!;
    public Attachment Attachment { get; set; } = null!;
}
