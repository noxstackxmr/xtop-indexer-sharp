using System.Buffers.Binary;

namespace IndexerCore.Protocol;

public sealed record XtopWitness(byte Kind, ushort Profile, byte[] Proof);
public sealed record XtopMessage(byte Operation, byte[] Payload, XtopWitness[] Witnesses);

public static class XtopMessageReader
{
    public static XtopMessage? ReadExtra(ReadOnlySpan<byte> extra, byte expectedNetwork)
    {
        XtopMessage? message = null;
        while (!extra.IsEmpty)
        {
            var tag = Take(ref extra, 1)[0];
            switch (tag)
            {
                case 0x00:
                    if (extra.Length > 254 || extra.ContainsAnyExcept((byte)0))
                        throw new FormatException("Invalid extra padding.");
                    extra = [];
                    break;
                case 0x01:
                    Take(ref extra, 32);
                    break;
                case 0x02:
                    Take(ref extra, ReadLength(ref extra, 255));
                    break;
                case 0x03:
                    Take(ref extra, ReadLength(ref extra, extra.Length));
                    break;
                case 0x04:
                    Take(ref extra, ReadLength(ref extra, extra.Length / 32) * 32);
                    break;
                case 0xDE:
                    var carrier = Take(ref extra, ReadLength(ref extra, extra.Length));
                    if (!carrier.StartsWith("XTOP"u8)) break;
                    if (message != null) throw new FormatException("Duplicate XTOP carrier.");
                    message = ReadMessage(carrier, expectedNetwork);
                    break;
                default:
                    // never search arbitrary bytes for magic
                    throw new FormatException($"Unsupported extra tag 0x{tag:X2}.");
            }
        }
        return message;
    }

    private static XtopMessage ReadMessage(ReadOnlySpan<byte> bytes, byte expectedNetwork)
    {
        if (bytes.Length > 1024) throw new FormatException("XTOP message exceeds 1024 bytes.");
        Take(ref bytes, 4);
        var version = Take(ref bytes, 1)[0];
        if (version != 1) throw new FormatException($"unsupported XTOP version {version}");
        var network = Take(ref bytes, 1)[0];
        if (network != expectedNetwork)
            throw new FormatException($"expected XTOP network {expectedNetwork}, got {network}");
        var operation = Take(ref bytes, 1)[0];
        var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(Take(ref bytes, 4));
        if (payloadLength > bytes.Length) throw new FormatException("Truncated XTOP payload.");
        var payload = Take(ref bytes, (int)payloadLength).ToArray();
        int count = Take(ref bytes, 1)[0];
        if (count > 32) throw new FormatException("Too many witnesses.");
        var witnesses = new XtopWitness[count];
        for (var i = 0; i < count; i++)
        {
            var kind = Take(ref bytes, 1)[0];
            var profile = BinaryPrimitives.ReadUInt16LittleEndian(Take(ref bytes, 2));
            int length = BinaryPrimitives.ReadUInt16LittleEndian(Take(ref bytes, 2));
            if (length is < 1 or > 4096) throw new FormatException("Invalid witness length.");
            witnesses[i] = new XtopWitness(kind, profile, [.. Take(ref bytes, length)]);
        }
        return !bytes.IsEmpty ? throw new FormatException("Unexpected trailing XTOP bytes.") : new XtopMessage(operation, payload, witnesses);
    }

    private static ReadOnlySpan<byte> Take(ref ReadOnlySpan<byte> bytes, int length)
    {
        if (length < 0 || length > bytes.Length) throw new FormatException("Truncated byte sequence.");
        var value = bytes[..length];
        bytes = bytes[length..];
        return value;
    }

    private static int ReadLength(ref ReadOnlySpan<byte> bytes, int maximum)
    {
        ulong value = 0;
        for (var i = 0; i < 10; i++)
        {
            var b = Take(ref bytes, 1)[0];
            if (i == 9 && b > 1) throw new FormatException("VarInt overflow.");
            value |= (ulong)(b & 127) << (7 * i);
            if ((b & 128) != 0) continue;
            if ((i > 0 && b == 0) || value > (ulong)maximum)
                throw new FormatException("Invalid extra field length.");
            return (int)value;
        }
        throw new FormatException("VarInt overflow.");
    }
}
