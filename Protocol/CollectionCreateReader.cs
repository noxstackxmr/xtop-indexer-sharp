namespace IndexerCore.Protocol;

public sealed record NewBinding(byte OutputIndex, byte[] KeyImage, byte[] OwnerKey, ulong NominalAmount,
    byte OwnershipWitness, byte AmountWitness);

public sealed record CollectionCreate(ChunkReference Terms, NewBinding Control, byte IssuanceRootKind,
    NewBinding IssuanceRoot, byte PaymentWitness);

public static class CollectionCreateReader
{
    public static CollectionCreate Read(XtopMessage message)
    {
        if (message.Operation != 0x02) throw new FormatException("expected COLLECTION_CREATE");
        var reader = new PayloadReader(message.Payload);
        var terms = reader.ReadChunkReference();
        var control = ReadBinding(ref reader);
        var rootKind = reader.ReadByte();
        if (rootKind > 1) throw new FormatException("invalid issuance root kind");
        var root = ReadBinding(ref reader);
        var paymentWitness = reader.ReadByte();
        reader.EnsureEnd();
        if (control.OutputIndex == root.OutputIndex || control.KeyImage.AsSpan().SequenceEqual(root.KeyImage))
            throw new FormatException("control and issuance root must use distinct outputs and key images");
        foreach (var witness in message.Witnesses)
        {
            if (witness.Kind is not (1 or 2 or 3 or 4 or 7) || witness.Profile == 0 || witness.Proof.Length is < 1 or > 4096)
                throw new FormatException("invalid witness header");
        }
        foreach (var binding in new[] { control, root })
        {
            RequireWitness(message, binding.OwnershipWitness, 1);
            RequireWitness(message, binding.AmountWitness, 2);
        }
        var payment = new PayloadReader(RequireWitness(message, paymentWitness, 4).Proof);
        var recipients = payment.ReadByte();
        if (recipients > 1) throw new FormatException("creation only supports a protocol fee recipient");
        if (recipients == 1)
        {
            if (payment.ReadByte() != 3) throw new FormatException("expected protocol fee recipient role");
            payment.Take(32);
            payment.Take(64);
        }
        payment.EnsureEnd();
        return new CollectionCreate(terms, control, rootKind, root, paymentWitness);
    }

    public static void ValidateSupply(CollectionCreate creation, uint maxSupply)
    {
        if (maxSupply == 0 || (creation.IssuanceRootKind == 1) != (maxSupply == 1))
            throw new FormatException("issuance root kind does not match max supply");
    }

    private static NewBinding ReadBinding(ref PayloadReader reader)
    {
        var index = reader.ReadByte();
        if (index > 15) throw new FormatException("binding output index exceeds 15");
        return new NewBinding(index, reader.Take(32).ToArray(), reader.Take(32).ToArray(),
            reader.ReadUInt64(), reader.ReadByte(), reader.ReadByte());
    }

    private static XtopWitness RequireWitness(XtopMessage message, byte index, byte kind)
    {
        if (index >= message.Witnesses.Length || message.Witnesses[index].Kind != kind)
            throw new FormatException($"expected witness kind {kind} at index {index}");
        return message.Witnesses[index];
    }
}
