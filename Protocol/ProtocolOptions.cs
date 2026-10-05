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
    public byte Version { get; set; } = 1;
    public ushort? PrimaryFeeBps { get; set; }
    public ushort? SecondaryFeeBps { get; set; }
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
        if (Network is not (0 or 1 or 2 or 255) || ActivationHeight < 0 || ControlAmount == 0 || NftAmount == 0 ||
            Version is not (1 or 2 or 3) || (Version == 1 && PrimaryFeeBps != null) ||
            (Version >= 2 && (PrimaryFeeBps == null || PrimaryFeeBps > 10000)) ||
            (Version < 3 && SecondaryFeeBps != null) || (Version == 3 && (SecondaryFeeBps == null || SecondaryFeeBps > 10000)))
            throw new FormatException("invalid creation configuration");
        var spend = Convert.FromHexString(FeeSpendKey);
        var view = Convert.FromHexString(FeeViewKey);
        MoneroProofCrypto.RequirePoint(spend);
        MoneroProofCrypto.RequirePoint(view);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Version switch { 1 => "XTOP:CONFIG:V1\0"u8, 2 => "XTOP:CONFIG:V2\0"u8, _ => "XTOP:CONFIG:V3\0"u8 });
        writer.Write(Network);
        writer.Write(XtopMessageReader.CurrentVersion);
        writer.Write(CollectionCreateProofs.Profile);
        writer.Write((ulong)ActivationHeight);
        writer.Write(spend);
        writer.Write(view);
        writer.Write(CreationFee);
        writer.Write(ControlAmount);
        writer.Write(NftAmount);
        if (Version >= 2)
        {
            writer.Write(PrimaryFeeBps!.Value);
            writer.Write(Sales.PrimarySaleProofs.Profile);
            writer.Write(Sales.PrimaryBatchProofs.Profile);
        }
        if (Version == 3)
        {
            writer.Write(SecondaryFeeBps!.Value);
            writer.Write(Sales.ListingProofs.ListProfile);
            writer.Write(Sales.ListingProofs.CancelProfile);
            writer.Write(Sales.SecondaryPurchaseProofs.Profile);
        }
        return stream.ToArray();
    }
}
