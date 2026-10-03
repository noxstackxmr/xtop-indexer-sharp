using System.Buffers.Binary;
using System.Numerics;
using Org.BouncyCastle.Crypto.Digests;

namespace IndexerCore.Protocol;

public static class DataChunkMerkle
{
    public static bool Verify(ParsedDataChunk chunk)
    {
        if (chunk.Hash.Length != 32 || chunk.MerkleRoot.Length != 32 ||
            chunk.Count == 0 || chunk.Index >= chunk.Count || chunk.Data.IsEmpty || chunk.Data.Length > 905)
            return false;

        var depth = BitOperations.Log2(BitOperations.RoundUpToPowerOf2((uint)chunk.Count));
        if (chunk.Proof.Length != depth * 32) return false;

        Span<byte> current = stackalloc byte[32];
        HashLeaf(chunk.Hash, chunk.TotalLength, chunk.Count, chunk.Index, chunk.Data.Span, current);
        for (var level = 0; level < depth; level++)
        {
            var sibling = chunk.Proof.Span.Slice(level * 32, 32);
            if (((chunk.Index >> level) & 1) == 0)
                HashNode(current, sibling, current);
            else
                HashNode(sibling, current, current);
        }

        return current.SequenceEqual(chunk.MerkleRoot);
    }

    internal static void HashLeaf(ReadOnlySpan<byte> hash, uint totalLength, ushort count, ushort index,
        ReadOnlySpan<byte> data, Span<byte> result)
    {
        var digest = Start(0);
        digest.BlockUpdate(hash);
        Span<byte> fields = stackalloc byte[10];
        BinaryPrimitives.WriteUInt32LittleEndian(fields, totalLength);
        BinaryPrimitives.WriteUInt16LittleEndian(fields[4..], count);
        BinaryPrimitives.WriteUInt16LittleEndian(fields[6..], index);
        BinaryPrimitives.WriteUInt16LittleEndian(fields[8..], checked((ushort)data.Length));
        digest.BlockUpdate(fields);
        digest.BlockUpdate(data);
        digest.DoFinal(result);
    }

    internal static void HashPadding(ReadOnlySpan<byte> hash, uint totalLength, ushort count, ushort index, Span<byte> result)
    {
        var digest = Start(2);
        digest.BlockUpdate(hash);
        Span<byte> fields = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(fields, totalLength);
        BinaryPrimitives.WriteUInt16LittleEndian(fields[4..], count);
        BinaryPrimitives.WriteUInt16LittleEndian(fields[6..], index);
        digest.BlockUpdate(fields);
        digest.DoFinal(result);
    }

    internal static void HashNode(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, Span<byte> result)
    {
        var digest = Start(1);
        digest.BlockUpdate(left);
        digest.BlockUpdate(right);
        digest.DoFinal(result);
    }

    private static KeccakDigest Start(byte kind)
    {
        var digest = new KeccakDigest(256);
        digest.BlockUpdate("XTOP:CHUNK:V1"u8);
        digest.Update(kind);
        return digest;
    }
}
