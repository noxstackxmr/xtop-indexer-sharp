using System.Numerics;
using Org.BouncyCastle.Crypto.Digests;

namespace IndexerCore.Protocol;

public sealed class AttachmentAssembly
{
    private readonly byte[] hash;
    private readonly uint totalLength;
    private readonly byte[] merkleRoot;
    private byte[]?[]? parts;
    private int received;
    private int receivedLength;

    public AttachmentAssembly(byte[] hash, uint totalLength, byte[] merkleRoot)
    {
        if (hash.Length != 32 || merkleRoot.Length != 32 || totalLength is < 1 or > 16_777_216)
            throw new FormatException("invalid attachment reference");
        this.hash = [.. hash];
        this.totalLength = totalLength;
        this.merkleRoot = [.. merkleRoot];
    }

    public void Add(ParsedDataChunk chunk)
    {
        if (chunk.TotalLength != totalLength || !chunk.Hash.AsSpan().SequenceEqual(hash) ||
            !chunk.MerkleRoot.AsSpan().SequenceEqual(merkleRoot))
            throw new FormatException("chunk does not match the attachment reference");
        if (!DataChunkMerkle.Verify(chunk)) throw new FormatException("invalid chunk Merkle proof");

        parts ??= new byte[chunk.Count][];
        if (parts.Length != chunk.Count) throw new FormatException("conflicting chunk counts");
        var previous = parts[chunk.Index];
        if (previous != null)
        {
            if (!chunk.Data.Span.SequenceEqual(previous)) throw new FormatException("conflicting chunk data");
            return;
        }
        if (receivedLength + chunk.Data.Length > totalLength)
            throw new FormatException("chunks exceed the attachment length");
        parts[chunk.Index] = chunk.Data.ToArray();
        receivedLength += chunk.Data.Length;
        received++;
    }

    public byte[]? Build(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (parts == null || received != parts.Length) return null;
        if (receivedLength != totalLength) throw new FormatException("attachment length does not match");

        var data = new byte[receivedLength];
        var leafCount = (int)BitOperations.RoundUpToPowerOf2((uint)parts.Length);
        var tree = new byte[leafCount * 32];
        var offset = 0;
        for (var i = 0; i < leafCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = tree.AsSpan(i * 32, 32);
            if (i < parts.Length)
            {
                var part = parts[i]!;
                part.CopyTo(data, offset);
                offset += part.Length;
                DataChunkMerkle.HashLeaf(hash, totalLength, (ushort)parts.Length, (ushort)i, part, destination);
            }
            else
            {
                DataChunkMerkle.HashPadding(hash, totalLength, (ushort)parts.Length, (ushort)i, destination);
            }
        }
        for (var width = leafCount; width > 1; width /= 2)
        {
            for (var i = 0; i < width / 2; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DataChunkMerkle.HashNode(tree.AsSpan(i * 64, 32), tree.AsSpan(i * 64 + 32, 32), tree.AsSpan(i * 32, 32));
            }
        }
        if (!tree.AsSpan(0, 32).SequenceEqual(merkleRoot))
            throw new FormatException("noncanonical attachment Merkle root");

        var digest = new KeccakDigest(256);
        digest.BlockUpdate(data);
        Span<byte> actualHash = stackalloc byte[32];
        digest.DoFinal(actualHash);
        if (!actualHash.SequenceEqual(hash)) throw new FormatException("attachment content hash does not match");
        return data;
    }
}
