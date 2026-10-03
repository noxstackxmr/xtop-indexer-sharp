using System.Security.Cryptography;
using IndexerCore.Monero;

namespace IndexerCore.Protocol;

public sealed record CollectionCreatePolicy(byte Network, byte[] ConfigHash, byte[] FeeSpendKey, byte[] FeeViewKey,
    ulong CreationFee, ulong ControlAmount, ulong NftAmount);

public static class CollectionCreateProofs
{
    public const ushort Profile = 0x0001;
    public static int MessageLength(ulong creationFee) => creationFee == 0 ? 610 : 707;

    public static byte[] Context(byte[] nativeBlob, XtopMessage message, CollectionCreatePolicy policy,
        ChunkReference expectedTerms, uint maxSupply)
    {
        var tx = MoneroProofTransaction.Parse(nativeBlob);
        Validate(message, tx, policy, expectedTerms, maxSupply);
        return BuildContext(tx, message, policy);
    }

    public static CollectionCreate Verify(byte[] nativeBlob, CollectionCreatePolicy policy,
        ChunkReference expectedTerms, uint maxSupply)
    {
        var tx = MoneroProofTransaction.Parse(nativeBlob);
        var message = XtopMessageReader.ReadMessage(tx.Message, policy.Network);
        var creation = Validate(message, tx, policy, expectedTerms, maxSupply);
        var context = BuildContext(tx, message, policy);
        foreach (var binding in new[] { creation.Control, creation.IssuanceRoot })
        {
            var output = tx.Outputs[binding.OutputIndex];
            var ownership = message.Witnesses[binding.OwnershipWitness].Proof;
            if (!MoneroProofCrypto.VerifyDleq(context, output.Key, MoneroProofCrypto.HashToPoint(output.Key), binding.KeyImage, ownership[..64]) ||
                !MoneroProofCrypto.VerifySignature(context, binding.OwnerKey, ownership[64..]))
                throw new CryptographicException("output binding proof failed");
            var mask = message.Witnesses[binding.AmountWitness].Proof;
            if (!MoneroProofCrypto.Commitment(mask, binding.NominalAmount).AsSpan().SequenceEqual(output.Commitment))
                throw new CryptographicException("binding amount commitment mismatch");
        }
        if (policy.CreationFee == 0) return creation;
        var payment = message.Witnesses[4].Proof;
        var d = payment[2..34];
        if (!MoneroProofCrypto.VerifyDleq(context, tx.TransactionKey, policy.FeeViewKey, d, payment[34..]))
            throw new CryptographicException("creation payment proof failed");
        var cofactor = new byte[32];
        cofactor[0] = 8;
        var derivation = MoneroProofCrypto.Multiply(d, cofactor);
        ulong paid = 0;
        for (var i = 0; i < tx.Outputs.Length; i++)
        {
            var output = tx.Outputs[i];
            if (!MoneroProofCrypto.OutputKey(derivation, (ulong)i, policy.FeeSpendKey).AsSpan().SequenceEqual(output.Key)) continue;
            if (i == creation.Control.OutputIndex || i == creation.IssuanceRoot.OutputIndex)
                throw new FormatException("fee cannot reuse a binding output");
            var (amount, mask) = MoneroProofCrypto.DecodeAmount(derivation, (ulong)i, output.EncryptedAmount);
            if (!MoneroProofCrypto.Commitment(mask, amount).AsSpan().SequenceEqual(output.Commitment))
                throw new CryptographicException("payment commitment mismatch");
            paid = checked(paid + amount);
        }
        if (paid != policy.CreationFee) throw new CryptographicException("creation fee was not paid exactly");
        return creation;
    }

    private static CollectionCreate Validate(XtopMessage message, MoneroProofTransaction tx, CollectionCreatePolicy policy,
        ChunkReference expectedTerms, uint maxSupply)
    {
        if (policy.Network is not (0 or 1 or 2 or 255) || policy.ConfigHash.Length != 32 || policy.ControlAmount == 0 || policy.NftAmount == 0)
            throw new FormatException("invalid creation policy");
        MoneroProofCrypto.RequirePoint(policy.FeeSpendKey);
        MoneroProofCrypto.RequirePoint(policy.FeeViewKey);
        if (message.Version != 14 || !message.ConfigHash.AsSpan().SequenceEqual(policy.ConfigHash))
            throw new FormatException("creation version or configuration mismatch");
        var creation = CollectionCreateReader.Read(message);
        if (creation.Terms.TotalLength != expectedTerms.TotalLength || !creation.Terms.Hash.AsSpan().SequenceEqual(expectedTerms.Hash) ||
            !creation.Terms.MerkleRoot.AsSpan().SequenceEqual(expectedTerms.MerkleRoot))
            throw new FormatException("creation terms reference mismatch");
        CollectionCreateReader.ValidateSupply(creation, maxSupply);
        if (message.Witnesses.Length != 5 || creation.Control.OwnershipWitness != 0 || creation.Control.AmountWitness != 1 ||
            creation.IssuanceRoot.OwnershipWitness != 2 || creation.IssuanceRoot.AmountWitness != 3 || creation.PaymentWitness != 4)
            throw new FormatException("unexpected CREATE_V1 witness layout");
        int[] sizes = [128, 32, 128, 32, policy.CreationFee == 0 ? 1 : 98];
        for (var i = 0; i < sizes.Length; i++)
            if (message.Witnesses[i].Profile != Profile || message.Witnesses[i].Proof.Length != sizes[i])
                throw new FormatException("unexpected CREATE_V1 profile or proof length");
        if (message.Witnesses[4].Proof[0] != (policy.CreationFee == 0 ? 0 : 1))
            throw new FormatException("payment recipients do not match creation fee");
        foreach (var binding in new[] { creation.Control, creation.IssuanceRoot })
        {
            if (binding.OutputIndex >= tx.Outputs.Length || binding.NominalAmount == 0)
                throw new FormatException("invalid binding output or nominal amount");
            MoneroProofCrypto.RequirePoint(binding.KeyImage);
            MoneroProofCrypto.RequirePoint(binding.OwnerKey);
        }
        if (creation.Control.NominalAmount != policy.ControlAmount ||
            (creation.IssuanceRootKind == 1 && creation.IssuanceRoot.NominalAmount != policy.NftAmount))
            throw new FormatException("binding nominal amount does not match policy");
        return creation;
    }

    private static byte[] BuildContext(MoneroProofTransaction tx, XtopMessage message, CollectionCreatePolicy policy)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("XTOP:CREATE:V1\0"u8);
        writer.Write("XTOP"u8);
        writer.Write(message.Version);
        writer.Write(policy.Network);
        writer.Write(message.ConfigHash);
        writer.Write(message.Operation);
        writer.Write(Profile);
        writer.Write(policy.FeeSpendKey);
        writer.Write(policy.FeeViewKey);
        writer.Write(policy.CreationFee);
        writer.Write(policy.ControlAmount);
        writer.Write(policy.NftAmount);
        writer.Write((uint)message.Payload.Length);
        writer.Write(message.Payload);
        writer.Write((byte)message.Witnesses.Length);
        foreach (var witness in message.Witnesses)
        {
            writer.Write(witness.Kind);
            writer.Write(witness.Profile);
            writer.Write((ushort)witness.Proof.Length);
        }
        writer.Write((uint)tx.PrefixWithoutCarrier.Length);
        writer.Write(tx.PrefixWithoutCarrier);
        writer.Write((uint)tx.RingCtBase.Length);
        writer.Write(tx.RingCtBase);
        return MoneroProofCrypto.Hash(stream.ToArray());
    }
}
