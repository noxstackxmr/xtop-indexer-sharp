using System.Text;

namespace IndexerCore.Protocol;

public sealed record StandardAddress(byte[] PublicSpendKey, byte[] PublicViewKey);
public sealed record ChunkReference(byte[] Hash, uint TotalLength, byte[] MerkleRoot);
public sealed record CollectionTerms(string Name, uint MaxSupply, byte MetadataMode, StandardAddress PrimaryPayout,
    StandardAddress RoyaltyPayout, ushort RoyaltyBps, byte ManagerPermissions, ChunkReference InitialLocations,
    ulong PrimaryPrice, ulong SaleStartUtc);

public static class CollectionTermsReader
{
    public static CollectionTerms Read(XtopMessage message)
    {
        if (message.Operation != 0xC0) throw new FormatException("expected COLLECTION_TERMS");
        var reader = new PayloadReader(message.Payload);
        var nameLength = reader.ReadByte();
        if (nameLength is < 1 or > 64) throw new FormatException("invalid collection name length");
        var nameBytes = reader.Take(nameLength);
        foreach (var value in nameBytes)
            if (value is < 0x20 or > 0x7E) throw new FormatException("collection name must be printable ASCII");
        if (nameBytes[0] == 0x20 || nameBytes[^1] == 0x20)
            throw new FormatException("collection name cannot start or end with a space");
        var name = Encoding.ASCII.GetString(nameBytes);
        var maxSupply = reader.ReadUInt32();
        if (maxSupply == 0) throw new FormatException("max supply must be positive");
        var metadataMode = reader.ReadByte();
        if (metadataMode > 1) throw new FormatException("invalid metadata mode");
        var primaryPayout = new StandardAddress(reader.Take(32).ToArray(), reader.Take(32).ToArray());
        var royaltyPayout = new StandardAddress(reader.Take(32).ToArray(), reader.Take(32).ToArray());
        var royaltyBps = reader.ReadUInt16();
        if (royaltyBps > 10000) throw new FormatException("royalty exceeds 10000 bps");
        var managerPermissions = reader.ReadByte();
        if (managerPermissions != 0) throw new FormatException("unsupported manager permissions");
        var locations = reader.ReadChunkReference();
        var primaryPrice = reader.ReadUInt64();
        var saleStartUtc = reader.ReadUInt64();
        if (saleStartUtc > 253402300799) throw new FormatException("sale start exceeds supported UTC range");
        reader.EnsureEnd();
        return new CollectionTerms(name, maxSupply, metadataMode, primaryPayout, royaltyPayout, royaltyBps,
            managerPermissions, locations, primaryPrice, saleStartUtc);
    }
}
