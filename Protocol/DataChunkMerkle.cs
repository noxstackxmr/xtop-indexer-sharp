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

        var digest = new KeccakDigest(256);
        digest.BlockUpdate("XTOP:CHUNK:V1"u8);
        digest.Update(0);
        digest.BlockUpdate(chunk.Hash);

        Span<byte> fields = stackalloc byte[10];
        BinaryPrimitives.WriteUInt32LittleEndian(fields, chunk.TotalLength);
        BinaryPrimitives.WriteUInt16LittleEndian(fields[4..], chunk.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(fields[6..], chunk.Index);
        BinaryPrimitives.WriteUInt16LittleEndian(fields[8..], (ushort)chunk.Data.Length);
        digest.BlockUpdate(fields);
        digest.BlockUpdate(chunk.Data.Span);

        Span<byte> current = stackalloc byte[32];
        digest.DoFinal(current);
        for (var level = 0; level < depth; level++)
        {
            var sibling = chunk.Proof.Span.Slice(level * 32, 32);
            digest.BlockUpdate("XTOP:CHUNK:V1"u8);
            digest.Update(1);
            if (((chunk.Index >> level) & 1) == 0)
            {
                digest.BlockUpdate(current);
                digest.BlockUpdate(sibling);
            }
            else
            {
                digest.BlockUpdate(sibling);
                digest.BlockUpdate(current);
            }
            digest.DoFinal(current);
        }

        return current.SequenceEqual(chunk.MerkleRoot);
    }
}
