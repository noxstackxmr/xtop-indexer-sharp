using System.Security.Cryptography;
using IndexerCore.Protocol.Collections;
using IndexerCore.Protocol.Messages;
using IndexerCore.Monero;

namespace IndexerCore.Protocol.Sales;

public sealed record PrimaryBatchResult(NewBinding[] Buyers, ulong CreatorAmount, ulong PlatformFee);

public static class PrimaryBatchProofs
{
    public const byte Operation = 0x13;
    public const ushort Profile = 0xFF08;
    private static bool Same(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);

    public static int MessageLength(int count, bool platformFee = true)
    {
        if (count is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(count));
        return 244 + 133 * count + (platformFee ? 97 : 0);
    }

    public static (ulong Creator, ulong Platform) Totals(PrimarySalePolicy[] policies)
    {
        _ = MessageLength(policies.Length);
        var first = policies[0];
        ulong creator = 0, platform = 0;
        var images = new HashSet<string>();
        foreach (var (p, i) in policies.Select((p, i) => (p, i)))
        {
            if (p.CollectionId.Length != 32 || p.Configuration.ConfigHash.Length != 32 || p.Terms.Hash.Length != 32 ||
                p.Terms.MerkleRoot.Length != 32 || p.Terms.TotalLength == 0 || p.Payout.Length != 64 || p.Price == 0 || p.FeeBps > 10000 ||
                p.Configuration.NftAmount == 0 || p.Current.NominalAmount != p.Configuration.NftAmount ||
                !Same(p.CollectionId, first.CollectionId) || !Same(p.Configuration.ConfigHash, first.Configuration.ConfigHash) ||
                p.Configuration.Network != first.Configuration.Network || p.Configuration.NftAmount != first.Configuration.NftAmount ||
                !Same(p.Configuration.FeeSpendKey, first.Configuration.FeeSpendKey) || !Same(p.Configuration.FeeViewKey, first.Configuration.FeeViewKey) ||
                !Same(p.Terms.Hash, first.Terms.Hash) || !Same(p.Terms.MerkleRoot, first.Terms.MerkleRoot) || p.Terms.TotalLength != first.Terms.TotalLength ||
                !Same(p.Payout, first.Payout) || p.Price != first.Price || p.FeeBps != first.FeeBps ||
                (i > 0 && policies[i - 1].Serial >= p.Serial) || !images.Add(Convert.ToHexString(p.Current.KeyImage)))
                throw new FormatException("batch requires ordered distinct NFTs from one collection policy");
            foreach (var point in new[] { p.Current.KeyImage, p.Current.OwnerKey, p.CurrentPublicKey, p.Payout[..32], p.Payout[32..], p.Configuration.FeeSpendKey, p.Configuration.FeeViewKey })
                MoneroProofCrypto.RequirePoint(point);
            creator = checked(creator + p.Price); platform = checked(platform + PrimarySaleProofs.Fee(p));
        }
        if (platform != 0 && Same(first.Payout, [.. first.Configuration.FeeSpendKey, .. first.Configuration.FeeViewKey]))
            throw new NotSupportedException("primary batch requires separate payout addresses");
        _ = checked(creator + platform);
        return (creator, platform);
    }

    public static byte[] Context(MoneroProofTransaction tx, XtopMessage message, PrimarySalePolicy[] policies)
        => MoneroProofCrypto.Hash(PrimarySaleEncoding.Write(w =>
        {
            w.Write("XTOP:PRIMARY:BATCH:LAB:V14\0"u8); w.Write("XTOP"u8); w.Write(message.Version);
            w.Write(policies[0].Configuration.Network); w.Write(message.ConfigHash); w.Write(message.Operation); w.Write(Profile);
            w.Write((byte)policies.Length);
            foreach (var p in policies)
            {
                w.Write(p.CollectionId); w.Write(p.Serial); w.Write(p.Current.OutputIndex); w.Write(p.Current.KeyImage);
                w.Write(p.Current.OwnerKey); w.Write(p.Current.NominalAmount); w.Write(p.Current.OwnershipWitness); w.Write(p.Current.AmountWitness);
                w.Write(p.CurrentPublicKey); PrimarySaleEncoding.Reference(w, p.Terms); w.Write(p.Payout); w.Write(p.Price);
                w.Write(p.Configuration.FeeSpendKey); w.Write(p.Configuration.FeeViewKey); w.Write(p.FeeBps); w.Write(p.Configuration.NftAmount);
            }
            w.Write((uint)message.Payload.Length); w.Write(message.Payload); w.Write((byte)message.Witnesses.Length);
            foreach (var proof in message.Witnesses) { w.Write(proof.Kind); w.Write(proof.Profile); w.Write((ushort)proof.Proof.Length); }
            w.Write((uint)tx.PrefixWithoutCarrier.Length); w.Write(tx.PrefixWithoutCarrier);
            w.Write((uint)tx.RingCtBase.Length); w.Write(tx.RingCtBase);
        }));

    public static PrimaryBatchResult Verify(MoneroProofTransaction tx, PrimarySalePolicy[] policies)
    {
        var totals = Totals(policies); var first = policies[0]; var count = policies.Length;
        var payments = Recipients(first, totals);
        var message = XtopMessageReader.ReadMessage(tx.Message, first.Configuration.Network);
        if (message.Version != first.Configuration.WireVersion || message.Operation != Operation || !Same(message.ConfigHash, first.Configuration.ConfigHash) ||
            tx.Message.Length != MessageLength(count, totals.Platform != 0) || message.Witnesses.Length != 1 ||
            message.Witnesses[0].Kind != 7 || message.Witnesses[0].Profile != Profile || message.Witnesses[0].Proof.Length != 65 + 96 * count + 97 * payments.Length ||
            tx.InputKeyImages.Length != count + 1 || tx.Outputs.Length != count + payments.Length + 1 ||
            tx.InputKeyImages.Select(Convert.ToHexString).Distinct().Count() != count + 1 ||
            policies.Any(p => tx.InputKeyImages.Count(i => Same(i, p.Current.KeyImage)) != 1))
            throw new FormatException("invalid batch envelope or native inputs/outputs");
        var r = new PayloadReader(message.Payload); var owner = r.Take(32).ToArray(); MoneroProofCrypto.RequirePoint(owner);
        if (r.ReadByte() != count) throw new FormatException("wrong batch count");
        var used = new HashSet<int>(); var images = new HashSet<string>(tx.InputKeyImages.Select(Convert.ToHexString));
        var buyers = new NewBinding[count];
        for (var i = 0; i < count; i++)
        {
            if (r.ReadUInt32() != policies[i].Serial) throw new FormatException("wrong batch serial");
            var index = r.ReadByte(); var image = r.Take(32).ToArray(); MoneroProofCrypto.RequirePoint(image);
            if (index >= tx.Outputs.Length || !used.Add(index) || !images.Add(Convert.ToHexString(image)) || (i > 0 && buyers[i - 1].OutputIndex >= index))
                throw new FormatException("buyer outputs must be fresh, distinct and ordered");
            buyers[i] = new NewBinding(index, image, owner, first.Configuration.NftAmount, 0, 0);
        }
        r.EnsureEnd();
        var context = Context(tx, message, policies); var proof = new PayloadReader(message.Witnesses[0].Proof);
        if (!MoneroProofCrypto.VerifySignature(context, owner, proof.Take(64).ToArray())) throw new CryptographicException("batch owner signature failed");
        foreach (var b in buyers)
        {
            var output = tx.Outputs[b.OutputIndex];
            if (!MoneroProofCrypto.VerifyDleq(context, output.Key, MoneroProofCrypto.HashToPoint(output.Key), b.KeyImage, proof.Take(64).ToArray()))
                throw new CryptographicException("batch output ownership failed");
            var mask = proof.Take(32).ToArray();
            if (!MoneroProofCrypto.IsScalar(mask) || !Same(MoneroProofCrypto.Commitment(mask, b.NominalAmount), output.Commitment))
                throw new CryptographicException("batch output amount failed");
        }
        if (proof.ReadByte() != payments.Length) throw new FormatException("wrong payment count");
        foreach (var (role, address, expected) in payments)
        {
            if (proof.ReadByte() != role) throw new FormatException("wrong payment role");
            var d = proof.Take(32).ToArray();
            if (!MoneroProofCrypto.VerifyDleq(context, tx.TransactionKey, address[32..], d, proof.Take(64).ToArray()))
                throw new CryptographicException("batch payment proof failed");
            var eight = new byte[32]; eight[0] = 8; var derivation = MoneroProofCrypto.Multiply(d, eight);
            ulong paid = 0;
            for (var i = 0; i < tx.Outputs.Length; i++)
            {
                var output = tx.Outputs[i];
                if (!Same(MoneroProofCrypto.OutputKey(derivation, (ulong)i, address[..32]), output.Key)) continue;
                if (!used.Add(i)) throw new FormatException("batch payment reused another role or NFT output");
                var (amount, amountMask) = MoneroProofCrypto.DecodeAmount(derivation, (ulong)i, output.EncryptedAmount);
                if (!Same(MoneroProofCrypto.Commitment(amountMask, amount), output.Commitment))
                    throw new CryptographicException("batch payment commitment failed");
                paid = checked(paid + amount);
            }
            if (paid != expected) throw new CryptographicException("batch payment amount mismatch");
        }
        proof.EnsureEnd();
        return new PrimaryBatchResult(buyers, totals.Creator, totals.Platform);
    }

    private static (byte Role, byte[] Address, ulong Amount)[] Recipients(PrimarySalePolicy first, (ulong Creator, ulong Platform) totals)
        => totals.Platform == 0 ? [(1, first.Payout, totals.Creator)] :
            [(1, first.Payout, totals.Creator), (3, [.. first.Configuration.FeeSpendKey, .. first.Configuration.FeeViewKey], totals.Platform)];
}
