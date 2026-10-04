using IndexerCore.Protocol.Messages;
using IndexerCore.Protocol.Collections;
using IndexerCore.Monero;

namespace IndexerCore.Protocol;

public sealed class ProtocolOptions
{
    public const string SectionName = "Protocol";
    public List<CreationConfiguration> Configurations { get; set; } = [];
}

public sealed class CreationConfiguration
{
    public byte Network { get; set; }
    public long ActivationHeight { get; set; }
    public string FeeSpendKey { get; set; } = "";
    public string FeeViewKey { get; set; } = "";
    public ulong CreationFee { get; set; }
    public ulong ControlAmount { get; set; }
    public ulong NftAmount { get; set; }
    public CollectionCreatePolicy BuildPolicy()
        => new(Network, MoneroProofCrypto.Hash(CanonicalBytes()), Convert.FromHexString(FeeSpendKey),
            Convert.FromHexString(FeeViewKey), CreationFee, ControlAmount, NftAmount);

    public byte[] CanonicalBytes()
    {
        if (Network is not (0 or 1 or 2 or 255) || ActivationHeight < 0 || ControlAmount == 0 || NftAmount == 0)
            throw new FormatException("invalid creation configuration");
        var spend = Convert.FromHexString(FeeSpendKey);
        var view = Convert.FromHexString(FeeViewKey);
        MoneroProofCrypto.RequirePoint(spend);
        MoneroProofCrypto.RequirePoint(view);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("XTOP:CONFIG:V1\0"u8);
        writer.Write(Network);
        writer.Write(XtopMessageReader.CurrentVersion);
        writer.Write(CollectionCreateProofs.Profile);
        writer.Write((ulong)ActivationHeight);
        writer.Write(spend);
        writer.Write(view);
        writer.Write(CreationFee);
        writer.Write(ControlAmount);
        writer.Write(NftAmount);
        return stream.ToArray();
    }
}
