using System.Security.Cryptography;
using IndexerCore.Monero;
using IndexerCore.Protocol.Messages;

namespace IndexerCore.Protocol.Collections;

public static class CollectionControlProofs
{
    public const ushort Profile = 0xFF05;

    public static CollectionControl Verify(byte[] nativeBlob, CollectionCreatePolicy policy, CollectionControlState previous)
    {
        var tx = MoneroProofTransaction.Parse(nativeBlob);
        var message = XtopMessageReader.ReadMessage(tx.Message, policy.Network);
        var step = Validate(tx, message, policy, previous);
        var context = Context(tx, message, previous, policy.Network);
        var output = tx.Outputs[step.Successor.OutputIndex];
        var proof = message.Witnesses[0].Proof;
        if (!MoneroProofCrypto.VerifyDleq(context, output.Key, MoneroProofCrypto.HashToPoint(output.Key), step.Successor.KeyImage, proof[..64]) ||
            !MoneroProofCrypto.VerifySignature(context, step.Successor.OwnerKey, proof[64..]))
            throw new CryptographicException("successor binding proof failed");
        if (!MoneroProofCrypto.Commitment(message.Witnesses[1].Proof, step.Successor.NominalAmount).AsSpan().SequenceEqual(output.Commitment))
            throw new CryptographicException("control amount commitment mismatch");
        if (!MoneroProofCrypto.VerifySignature(context, previous.Binding.OwnerKey, message.Witnesses[2].Proof))
            throw new CryptographicException("current manager authorization failed");
        return step;
    }

    private static CollectionControl Validate(MoneroProofTransaction tx, XtopMessage message, CollectionCreatePolicy policy, CollectionControlState previous)
    {
        if (message.Version != policy.WireVersion || !message.ConfigHash.AsSpan().SequenceEqual(policy.ConfigHash))
            throw new FormatException("control version or configuration mismatch");
        var step = CollectionControlReader.Read(message);
        if (previous.CollectionId.Length != 32 || !step.CollectionId.AsSpan().SequenceEqual(previous.CollectionId) ||
            !step.PreviousKeyImage.AsSpan().SequenceEqual(previous.Binding.KeyImage))
            throw new FormatException("current collection control mismatch");
        foreach (var point in new[] { previous.PublicKey, previous.Binding.KeyImage, previous.Binding.OwnerKey,
                     step.Successor.KeyImage, step.Successor.OwnerKey }) MoneroProofCrypto.RequirePoint(point);
        if (tx.InputKeyImages.Count(i => i.AsSpan().SequenceEqual(previous.Binding.KeyImage)) != 1)
            throw new FormatException("transaction must spend the current control exactly once");
        if (step.Successor.OutputIndex >= tx.Outputs.Length || step.Successor.KeyImage.AsSpan().SequenceEqual(previous.Binding.KeyImage) ||
            policy.ControlAmount == 0 || previous.Binding.NominalAmount != policy.ControlAmount || step.Successor.NominalAmount != policy.ControlAmount)
            throw new FormatException("invalid control successor");
        if (message.Operation != 0x0F && !step.Successor.OwnerKey.AsSpan().SequenceEqual(previous.Binding.OwnerKey))
            throw new FormatException("only CONTROL_TRANSFER may change the manager");
        if (step.Successor.OwnershipWitness != 0 || step.Successor.AmountWitness != 1 || message.Witnesses.Length != 3)
            throw new FormatException("unexpected control witness layout");
        byte[] kinds = [1, 2, 3]; int[] lengths = [128, 32, 64];
        for (var i = 0; i < 3; i++)
        {
            if (message.Witnesses[i].Profile != Profile) throw new NotSupportedException("control proof profile is not supported");
            if (message.Witnesses[i].Kind != kinds[i] || message.Witnesses[i].Proof.Length != lengths[i])
                throw new FormatException("unexpected control proof layout");
        }
        return step;
    }

    public static byte[] Context(MoneroProofTransaction tx, XtopMessage message, CollectionControlState previous, byte network)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("XTOP:CONTROL:LAB:V1\0"u8);
        writer.Write("XTOP"u8); writer.Write(message.Version); writer.Write(network);
        writer.Write(message.ConfigHash); writer.Write(message.Operation); writer.Write(Profile);
        writer.Write(previous.CollectionId); writer.Write(previous.PublicKey); writer.Write(previous.Binding.KeyImage);
        writer.Write(previous.Binding.OwnerKey); writer.Write(previous.Binding.NominalAmount);
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
