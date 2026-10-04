namespace IndexerCore.Monero;

public static class MoneroInputReader
{
    public static byte[][] ReadKeyImages(byte[] data)
    {
        var reader = new NativeByteReader(data);
        if (reader.VarInt() is not (1 or 2)) throw new FormatException("unsupported native transaction version");
        _ = reader.VarInt();
        var images = new byte[reader.Count(reader.Remaining)][];
        for (var i = 0; i < images.Length; i++)
        {
            if (reader.Byte() != 2) throw new FormatException("unsupported native input type");
            _ = reader.VarInt();
            var offsets = reader.Count(reader.Remaining);
            for (var j = 0; j < offsets; j++) _ = reader.VarInt();
            images[i] = reader.Bytes(32);
        }
        return images;
    }
}
