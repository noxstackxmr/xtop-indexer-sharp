using System.Security.Cryptography;
using IndexerCore.Monero;
using IndexerCore.Protocol.Collections;
using IndexerCore.Protocol.Messages;

namespace IndexerCore.Protocol.Issuance;

public sealed record CompactIssueChild(uint Count, byte OutputIndex, byte[] KeyImage);
public sealed record CompactIssuePayload(byte[] CollectionId, byte[] ParentKeyImage, CompactIssueChild[] Children);

public static class CompactIssueSplitProofs
{
    public const ushort Profile = 0xFF06;
    public static int MessageLength(int children) => children is >= 2 and <= 6
        ? 178 + 133 * children : throw new ArgumentOutOfRangeException(nameof(children));

    public static CompactIssuePayload Read(XtopMessage message)
    {
        if (message.Operation != 0x12) throw new FormatException("expected ISSUE_SPLIT");
        var reader = new PayloadReader(message.Payload);
        var collection = reader.Take(32).ToArray();
        var parent = reader.Take(32).ToArray();
        var count = reader.ReadByte();
        if (count is < 2 or > 6) throw new FormatException("compact split requires two to six children");
        var children = new CompactIssueChild[count];
        for (var i = 0; i < count; i++)
            children[i] = new(reader.ReadUInt32(), reader.ReadByte(), reader.Take(32).ToArray());
        reader.EnsureEnd();
        return new(collection, parent, children);
    }

    public static IssueSplit Verify(byte[] blob, CollectionCreatePolicy policy, IssuanceState parent)
        => Verify(MoneroProofTransaction.Parse(blob), policy, parent);

    public static IssueSplit Verify(MoneroProofTransaction tx, CollectionCreatePolicy policy, IssuanceState parent)
    {
        var message = XtopMessageReader.ReadMessage(tx.Message, policy.Network);
        var split = Validate(tx, message, policy, parent);
        var context = Context(tx, message, policy, parent);
        var proof = message.Witnesses[0].Proof;
        if (!MoneroProofCrypto.VerifySignature(context, parent.Binding.OwnerKey, proof[..64]))
            throw new CryptographicException("compact owner signature failed");
        for (var i = 0; i < split.Children.Length; i++)
        {
            var binding = split.Children[i].Binding;
            var output = tx.Outputs[binding.OutputIndex];
            var offset = 64 + 96 * i;
            if (!MoneroProofCrypto.VerifyDleq(context, output.Key, MoneroProofCrypto.HashToPoint(output.Key),
                    binding.KeyImage, proof[offset..(offset + 64)]))
                throw new CryptographicException("compact child ownership proof failed");
            if (!MoneroProofCrypto.Commitment(proof[(offset + 64)..(offset + 96)], policy.NftAmount)
                    .AsSpan().SequenceEqual(output.Commitment))
                throw new CryptographicException("compact child amount mismatch");
        }
        return split;
    }

    private static IssueSplit Validate(MoneroProofTransaction tx, XtopMessage message, CollectionCreatePolicy policy, IssuanceState parent)
    {
        if (message.Version != policy.WireVersion || !message.ConfigHash.AsSpan().SequenceEqual(policy.ConfigHash) || policy.NftAmount == 0)
            throw new FormatException("compact version or configuration mismatch");
        var payload = Read(message);
        if (message.Witnesses.Length != 1 || message.Witnesses[0].Kind != 7 ||
            message.Witnesses[0].Profile != Profile || message.Witnesses[0].Proof.Length != 64 + 96 * payload.Children.Length)
            throw new FormatException("unexpected compact witness layout");
        if (parent.CollectionId.Length != 32 || parent.Count < 2 || parent.Binding.NominalAmount == 0 ||
            !payload.CollectionId.AsSpan().SequenceEqual(parent.CollectionId) ||
            !payload.ParentKeyImage.AsSpan().SequenceEqual(parent.Binding.KeyImage))
            throw new FormatException("compact parent mismatch");
        foreach (var point in new[] { parent.PublicKey, parent.Binding.KeyImage, parent.Binding.OwnerKey }) MoneroProofCrypto.RequirePoint(point);
        if (tx.InputKeyImages.Count(i => i.AsSpan().SequenceEqual(parent.Binding.KeyImage)) != 1)
            throw new FormatException("compact split must spend its parent exactly once");
        ulong cursor = parent.FirstSerial, end = cursor + parent.Count;
        if (end > (ulong)uint.MaxValue + 1) throw new FormatException("parent range overflow");
        var indices = new HashSet<byte>();
        var images = new HashSet<string>();
        var children = new IssuanceChild[payload.Children.Length];
        for (var i = 0; i < children.Length; i++)
        {
            var child = payload.Children[i];
            if (child.Count == 0 || cursor + child.Count > end || child.OutputIndex > 15 ||
                child.OutputIndex >= tx.Outputs.Length || !indices.Add(child.OutputIndex) ||
                !images.Add(Convert.ToHexString(child.KeyImage)) || tx.InputKeyImages.Any(k => k.AsSpan().SequenceEqual(child.KeyImage)))
                throw new FormatException("invalid compact child range or binding");
            MoneroProofCrypto.RequirePoint(child.KeyImage);
            children[i] = new(checked((uint)cursor), child.Count,
                new(child.OutputIndex, child.KeyImage, parent.Binding.OwnerKey.ToArray(), policy.NftAmount, 0, 0),
                child.Count == 1 ? (byte)1 : (byte)0);
            cursor += child.Count;
        }
        if (cursor != end) throw new FormatException("incomplete compact range coverage");
        return new(parent.CollectionId, parent.Binding.KeyImage, children);
    }

    public static byte[] Context(MoneroProofTransaction tx, XtopMessage message, CollectionCreatePolicy policy, IssuanceState parent)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("XTOP:SPLIT:COMPACT:LAB:V1\0"u8);
        w.Write("XTOP"u8); w.Write(message.Version); w.Write(policy.Network); w.Write(policy.ConfigHash);
        w.Write(message.Operation); w.Write(Profile);
        w.Write(parent.CollectionId); w.Write(parent.FirstSerial); w.Write(parent.Count);
        w.Write(parent.PublicKey); w.Write(parent.Binding.KeyImage); w.Write(parent.Binding.OwnerKey);
        w.Write(parent.Binding.NominalAmount); w.Write(policy.NftAmount);
        w.Write((uint)message.Payload.Length); w.Write(message.Payload); w.Write((byte)message.Witnesses.Length);
        foreach (var witness in message.Witnesses) { w.Write(witness.Kind); w.Write(witness.Profile); w.Write((ushort)witness.Proof.Length); }
        w.Write((uint)tx.PrefixWithoutCarrier.Length); w.Write(tx.PrefixWithoutCarrier);
        w.Write((uint)tx.RingCtBase.Length); w.Write(tx.RingCtBase);
        return MoneroProofCrypto.Hash(stream.ToArray());
    }
}
