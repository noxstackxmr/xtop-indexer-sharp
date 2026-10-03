using System.Buffers.Binary;
using System.Numerics;

namespace IndexerCore.Protocol;

public sealed record ParsedDataChunk(
    byte[] Hash, uint TotalLength, byte[] MerkleRoot, ushort Index, ushort Count,
    ReadOnlyMemory<byte> Data, ReadOnlyMemory<byte> Proof);

public static class DataChunkReader
{
    public static ParsedDataChunk Read(XtopMessage message)
    {
        if (message.Version != XtopMessageReader.CurrentVersion)
            throw new NotSupportedException($"unsupported DATA_CHUNK version {message.Version}");
        if (message.Operation != 0x01 || message.Witnesses.Length != 0)
            throw new FormatException("expected DATA_CHUNK without witnesses");

        var payload = message.Payload.AsSpan();
        if (payload.Length < 75)
            throw new FormatException("truncated DATA_CHUNK header");
        if (payload.Length > 980)
            throw new FormatException("DATA_CHUNK exceeds the wire message limit");

        var totalLength = BinaryPrimitives.ReadUInt32LittleEndian(payload[32..]);
        var index = BinaryPrimitives.ReadUInt16LittleEndian(payload[68..]);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(payload[70..]);
        var length = BinaryPrimitives.ReadUInt16LittleEndian(payload[72..]);

        if (totalLength is < 1 or > 16_777_216)
            throw new FormatException("invalid attachment length");
        if (count == 0 || index >= count || count > totalLength)
            throw new FormatException("invalid chunk index or count");
        if (length == 0 || length > payload.Length - 75)
            throw new FormatException("invalid chunk length");
        if ((uint)length + count - 1 > totalLength || (count == 1 && length != totalLength))
            throw new FormatException("chunk sizes cannot match the attachment length");

        var depth = BitOperations.Log2(BitOperations.RoundUpToPowerOf2((uint)count));
        if (payload[74 + length] != depth || payload.Length != 75 + length + depth * 32)
            throw new FormatException("invalid Merkle proof length");
        if (totalLength > count * (905 - 32 * depth))
            throw new FormatException("attachment exceeds the chunk set capacity");

        return new ParsedDataChunk(
            [.. payload[..32]], totalLength, [.. payload.Slice(36, 32)], index, count,
            message.Payload.AsMemory(74, length), message.Payload.AsMemory(75 + length));
    }
}
