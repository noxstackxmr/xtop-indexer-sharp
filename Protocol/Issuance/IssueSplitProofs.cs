using System.Security.Cryptography;
using IndexerCore.Monero;
using IndexerCore.Protocol.Collections;
using IndexerCore.Protocol.Messages;

namespace IndexerCore.Protocol.Issuance;

public static class IssueSplitProofs
{
    public const ushort Profile = 0x0002;
    public static int MessageLength(int children) => children is 2 or 3 ? 109 + 254 * children : throw new ArgumentOutOfRangeException(nameof(children));

    public static IssueSplit Verify(byte[] nativeBlob, CollectionCreatePolicy policy, IssuanceState parent)
    {
        var tx = MoneroProofTransaction.Parse(nativeBlob);
        var message = XtopMessageReader.ReadMessage(tx.Message, policy.Network);
        if (IssueSplitReader.ReadProfile(message) == CompactIssueSplitProofs.Profile)
            return CompactIssueSplitProofs.Verify(tx, policy, parent);
        var split = Validate(tx, message, policy, parent);
        var context = Context(tx, message, policy, parent);
        foreach (var child in split.Children)
        {
            var binding = child.Binding;
            var output = tx.Outputs[binding.OutputIndex];
            var ownership = message.Witnesses[binding.OwnershipWitness].Proof;
            if (!MoneroProofCrypto.VerifyDleq(context, output.Key, MoneroProofCrypto.HashToPoint(output.Key), binding.KeyImage, ownership[..64]) ||
                !MoneroProofCrypto.VerifySignature(context, binding.OwnerKey, ownership[64..]))
                throw new CryptographicException("child ownership proof failed");
            if (!MoneroProofCrypto.Commitment(message.Witnesses[binding.AmountWitness].Proof, binding.NominalAmount).AsSpan().SequenceEqual(output.Commitment))
                throw new CryptographicException("child amount commitment mismatch");
        }
        return split;
    }

    private static IssueSplit Validate(MoneroProofTransaction tx, XtopMessage message, CollectionCreatePolicy policy, IssuanceState parent)
    {
        if (message.Version != policy.WireVersion || !message.ConfigHash.AsSpan().SequenceEqual(policy.ConfigHash) || policy.NftAmount == 0)
            throw new FormatException("split version or configuration mismatch");
        var split = IssueSplitReader.Read(message);
        if (parent.CollectionId.Length != 32 || parent.Count < 2 || parent.Binding.NominalAmount == 0 ||
            !split.CollectionId.AsSpan().SequenceEqual(parent.CollectionId) || !split.ParentKeyImage.AsSpan().SequenceEqual(parent.Binding.KeyImage))
            throw new FormatException("split parent or collection mismatch");
        foreach (var point in new[] { parent.PublicKey, parent.Binding.KeyImage, parent.Binding.OwnerKey }) MoneroProofCrypto.RequirePoint(point);
        if (tx.InputKeyImages.Count(i => i.AsSpan().SequenceEqual(parent.Binding.KeyImage)) != 1)
            throw new FormatException("split must spend its parent exactly once");
        if (message.Witnesses.Length != split.Children.Length * 2) throw new FormatException("unexpected split witness count");
        ulong cursor = parent.FirstSerial, end = cursor + parent.Count;
        if (end > (ulong)uint.MaxValue + 1) throw new FormatException("parent range overflow");
        var indices = new HashSet<byte>();
        var images = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < split.Children.Length; i++)
        {
            var child = split.Children[i];
            var binding = child.Binding;
            if (child.Count == 0 || child.FirstSerial != cursor || cursor + child.Count > end || child.Kind != (child.Count == 1 ? 1 : 0))
                throw new FormatException("children must cover the parent in order without gaps or overlap");
            cursor += child.Count;
            if (binding.OutputIndex > 15 || binding.OutputIndex >= tx.Outputs.Length || binding.NominalAmount != policy.NftAmount ||
                binding.OwnershipWitness != 2 * i || binding.AmountWitness != 2 * i + 1 || !indices.Add(binding.OutputIndex) ||
                !images.Add(Convert.ToHexString(binding.KeyImage)) || tx.InputKeyImages.Any(k => k.AsSpan().SequenceEqual(binding.KeyImage)))
                throw new FormatException("invalid or repeated child binding");
            MoneroProofCrypto.RequirePoint(binding.KeyImage);
            MoneroProofCrypto.RequirePoint(binding.OwnerKey);
            var ownership = message.Witnesses[2 * i];
            var amount = message.Witnesses[2 * i + 1];
            if (ownership.Profile != Profile || amount.Profile != Profile) throw new NotSupportedException("split proof profile is not supported");
            if (ownership.Kind != 1 || ownership.Proof.Length != 128 || amount.Kind != 2 || amount.Proof.Length != 32)
                throw new FormatException("unexpected split witness layout");
        }
        if (cursor != end) throw new FormatException("incomplete parent range coverage");
        return split;
    }

    public static byte[] Context(MoneroProofTransaction tx, XtopMessage message, CollectionCreatePolicy policy, IssuanceState parent)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("XTOP:SPLIT:V1\0"u8);
        writer.Write("XTOP"u8); writer.Write(message.Version); writer.Write(policy.Network);
        writer.Write(message.ConfigHash); writer.Write(message.Operation); writer.Write(Profile);
        writer.Write(parent.CollectionId); writer.Write(parent.FirstSerial); writer.Write(parent.Count);
        writer.Write(parent.PublicKey); writer.Write(parent.Binding.KeyImage); writer.Write(parent.Binding.OwnerKey);
        writer.Write(parent.Binding.NominalAmount); writer.Write(policy.NftAmount);
        writer.Write((uint)message.Payload.Length); writer.Write(message.Payload); writer.Write((byte)message.Witnesses.Length);
        foreach (var witness in message.Witnesses)
        {
            writer.Write(witness.Kind); writer.Write(witness.Profile); writer.Write((ushort)witness.Proof.Length);
        }
        writer.Write((uint)tx.PrefixWithoutCarrier.Length); writer.Write(tx.PrefixWithoutCarrier);
        writer.Write((uint)tx.RingCtBase.Length); writer.Write(tx.RingCtBase);
        return MoneroProofCrypto.Hash(stream.ToArray());
    }
}
