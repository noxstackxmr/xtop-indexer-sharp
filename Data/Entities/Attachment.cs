namespace IndexerCore.Data.Entities;

public enum AttachmentStatus : byte
{
    Incomplete,
    Complete,
    Invalid
}

public sealed class Attachment
{
    public long Id { get; set; }
    public byte Network { get; set; }
    public required byte[] Hash { get; set; }
    public required byte[] MerkleRoot { get; set; }
    public long TotalLength { get; set; }
    public byte? Type { get; set; }
    public AttachmentStatus Status { get; set; } = AttachmentStatus.Incomplete;

    public ICollection<DataChunk> Chunks { get; set; } = [];
}
