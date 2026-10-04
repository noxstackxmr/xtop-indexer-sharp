using System.Security.Cryptography;
using IndexerCore.Protocol.Attachments;
using IndexerCore.Protocol.Collections;
using IndexerCore.Protocol.Messages;
using IndexerCore.Monero;

namespace IndexerCore.Protocol.Sales;

public sealed record PrimarySalePolicy(CollectionCreatePolicy Configuration, byte[] CollectionId, uint Serial,
    NewBinding Current, byte[] CurrentPublicKey, ChunkReference Terms, byte[] Payout, ulong Price, ushort FeeBps);
public sealed record PrimarySaleResult(byte[] ItemId, NewBinding Buyer, byte[] Session, ulong CreatorAmount, ulong PlatformFee);

public static class PrimarySaleProofs
{
    public const ushort Profile = 0xFF07;
    public static ulong Fee(PrimarySalePolicy policy) => checked((ulong)((UInt128)policy.Price * policy.FeeBps / 10_000));
    public static int MessageLength(PrimarySalePolicy policy) => Fee(policy) == 0 ? 562 : 659;
    public static byte[] ItemId(byte[] collection, uint serial) => Items.ItemIdentity.Derive(collection, serial);

    private static void Binding(BinaryWriter w, NewBinding b)
    {
        w.Write(b.OutputIndex); w.Write(b.KeyImage); w.Write(b.OwnerKey); w.Write(b.NominalAmount);
        w.Write(b.OwnershipWitness); w.Write(b.AmountWitness);
    }

    public static byte[] Context(MoneroProofTransaction tx, XtopMessage message, PrimarySalePolicy policy)
        => MoneroProofCrypto.Hash(PrimarySaleEncoding.Write(w =>
        {
            w.Write("XTOP:PRIMARY:LAB:V14\0"u8); w.Write("XTOP"u8); w.Write(message.Version);
            w.Write(policy.Configuration.Network); w.Write(message.ConfigHash); w.Write(message.Operation); w.Write(Profile);
            w.Write(policy.CollectionId); w.Write(policy.Serial); Binding(w, policy.Current); w.Write(policy.CurrentPublicKey);
            PrimarySaleEncoding.Reference(w, policy.Terms); w.Write(policy.Payout); w.Write(policy.Price);
            w.Write(policy.Configuration.FeeSpendKey); w.Write(policy.Configuration.FeeViewKey); w.Write(policy.FeeBps);
            w.Write(policy.Configuration.NftAmount); w.Write((uint)message.Payload.Length); w.Write(message.Payload);
            w.Write((byte)message.Witnesses.Length);
            foreach (var witness in message.Witnesses) { w.Write(witness.Kind); w.Write(witness.Profile); w.Write((ushort)witness.Proof.Length); }
            w.Write((uint)tx.PrefixWithoutCarrier.Length); w.Write(tx.PrefixWithoutCarrier);
            w.Write((uint)tx.RingCtBase.Length); w.Write(tx.RingCtBase);
        }));

    public static PrimarySaleResult Verify(MoneroProofTransaction tx, PrimarySalePolicy policy)
    {
        var message = XtopMessageReader.ReadMessage(tx.Message, policy.Configuration.Network);
        var sale = Validate(tx, message, policy);
        var context = Context(tx, message, policy);
        var binding = sale.Buyer; var output = tx.Outputs[binding.OutputIndex];
        var proof = message.Witnesses[0].Proof;
        if (!MoneroProofCrypto.VerifyDleq(context, output.Key, MoneroProofCrypto.HashToPoint(output.Key), binding.KeyImage, proof[..64]) ||
            !MoneroProofCrypto.VerifySignature(context, binding.OwnerKey, proof[64..]))
            throw new CryptographicException("buyer binding proof failed");
        var mask = message.Witnesses[1].Proof;
        if (!MoneroProofCrypto.IsScalar(mask) || !MoneroProofCrypto.Commitment(mask, binding.NominalAmount).AsSpan().SequenceEqual(output.Commitment))
            throw new CryptographicException("buyer amount opening failed");
        var reader = new PayloadReader(message.Witnesses[2].Proof);
        var recipients = Recipients(policy);
        if (reader.ReadByte() != recipients.Length) throw new FormatException("unexpected payment count");
        var used = new HashSet<int> { binding.OutputIndex };
        foreach (var (role, address, expected) in recipients)
        {
            if (reader.ReadByte() != role) throw new FormatException("unexpected payment role");
            var d = reader.Take(32).ToArray(); var pi = reader.Take(64).ToArray();
            if (!MoneroProofCrypto.VerifyDleq(context, tx.TransactionKey, address[32..], d, pi))
                throw new CryptographicException("payment proof failed");
            var eight = new byte[32]; eight[0] = 8;
            var derivation = MoneroProofCrypto.Multiply(d, eight);
            ulong paid = 0;
            for (var i = 0; i < tx.Outputs.Length; i++)
            {
                var candidate = tx.Outputs[i];
                if (!MoneroProofCrypto.OutputKey(derivation, (ulong)i, address[..32]).AsSpan().SequenceEqual(candidate.Key)) continue;
                if (!used.Add(i)) throw new FormatException("payment reuses another role or NFT output");
                var (amount, amountMask) = MoneroProofCrypto.DecodeAmount(derivation, (ulong)i, candidate.EncryptedAmount);
                if (!MoneroProofCrypto.Commitment(amountMask, amount).AsSpan().SequenceEqual(candidate.Commitment))
                    throw new CryptographicException("payment commitment mismatch");
                paid = checked(paid + amount);
            }
            if (paid != expected) throw new CryptographicException("payment amount mismatch");
        }
        reader.EnsureEnd();
        return sale;
    }

    private static (byte Role, byte[] Address, ulong Amount)[] Recipients(PrimarySalePolicy policy)
    {
        var list = new List<(byte, byte[], ulong)> { (1, policy.Payout, policy.Price) };
        if (Fee(policy) != 0) list.Add((3, [.. policy.Configuration.FeeSpendKey, .. policy.Configuration.FeeViewKey], Fee(policy)));
        return [.. list];
    }

    private static PrimarySaleResult Validate(MoneroProofTransaction tx, XtopMessage message, PrimarySalePolicy policy)
    {
        var config = policy.Configuration;
        if (policy.CollectionId.Length != 32 || policy.Terms.Hash.Length != 32 || policy.Terms.MerkleRoot.Length != 32 ||
            policy.Terms.TotalLength == 0 || policy.Payout.Length != 64 || policy.Price == 0 || policy.FeeBps > 10_000 ||
            config.NftAmount == 0 || policy.Current.NominalAmount != config.NftAmount)
            throw new FormatException("invalid primary policy");
        _ = checked(policy.Price + Fee(policy));
        foreach (var point in new[] { policy.CurrentPublicKey, policy.Current.KeyImage, policy.Current.OwnerKey,
            policy.Payout[..32], policy.Payout[32..], config.FeeSpendKey, config.FeeViewKey }) MoneroProofCrypto.RequirePoint(point);
        if (Fee(policy) != 0 && policy.Payout.AsSpan().SequenceEqual([.. config.FeeSpendKey, .. config.FeeViewKey]))
            throw new NotSupportedException("primary profile requires separate payout addresses");
        if (message.Version != 14 || message.Operation != 9 || !message.ConfigHash.AsSpan().SequenceEqual(config.ConfigHash) ||
            message.Witnesses.Length != 3 || tx.InputKeyImages.Length < 2 ||
            tx.InputKeyImages.Count(i => i.AsSpan().SequenceEqual(policy.Current.KeyImage)) != 1)
            throw new FormatException("invalid primary envelope or native parent spend");
        var sizes = new[] { 128, 32, 1 + 97 * Recipients(policy).Length };
        for (var i = 0; i < 3; i++)
            if (message.Witnesses[i].Kind != new byte[] { 1, 2, 4 }[i] || message.Witnesses[i].Profile != Profile || message.Witnesses[i].Proof.Length != sizes[i])
                throw new FormatException("invalid primary witness layout");
        var reader = new PayloadReader(message.Payload);
        if (reader.ReadByte() != 2 || !reader.Take(32).SequenceEqual(ItemId(policy.CollectionId, policy.Serial)) ||
            !reader.Take(32).SequenceEqual(policy.Current.KeyImage) || reader.ReadUInt64() != policy.Price || !reader.Take(64).SequenceEqual(policy.Payout))
            throw new FormatException("primary terms mismatch");
        var session = reader.Take(32).ToArray();
        var buyer = new NewBinding(reader.ReadByte(), reader.Take(32).ToArray(), reader.Take(32).ToArray(), reader.ReadUInt64(), reader.ReadByte(), reader.ReadByte());
        if (reader.ReadByte() != 2) throw new FormatException("unexpected settlement witness");
        reader.EnsureEnd();
        if (buyer.OutputIndex > 15 || buyer.OutputIndex >= tx.Outputs.Length || buyer.NominalAmount != config.NftAmount ||
            buyer.OwnershipWitness != 0 || buyer.AmountWitness != 1 || tx.InputKeyImages.Any(i => i.AsSpan().SequenceEqual(buyer.KeyImage)))
            throw new FormatException("invalid buyer binding");
        MoneroProofCrypto.RequirePoint(buyer.KeyImage); MoneroProofCrypto.RequirePoint(buyer.OwnerKey);
        return new(ItemId(policy.CollectionId, policy.Serial), buyer, session, policy.Price, Fee(policy));
    }
}
