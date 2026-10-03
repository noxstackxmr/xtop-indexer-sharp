using System.Text;

namespace IndexerCore.Protocol;

public sealed record MediaLocation(byte Role, string Uri);

public static class MediaLocationsReader
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static MediaLocation[] Read(XtopMessage message)
    {
        if (message.Operation != 0xC1) throw new FormatException("expected MEDIA_LOCATIONS");
        var reader = new PayloadReader(message.Payload);
        var count = reader.ReadByte();
        if (count is < 1 or > 3) throw new FormatException("invalid location count");
        var locations = new MediaLocation[count];
        byte previousRole = 0;
        for (var i = 0; i < count; i++)
        {
            var role = reader.ReadByte();
            if (role is < 1 or > 3 || role <= previousRole)
                throw new FormatException("location roles must be unique and increasing");
            var length = reader.ReadUInt16();
            if (length is < 1 or > 1024) throw new FormatException("invalid URI byte length");
            string text;
            try { text = Utf8.GetString(reader.Take(length)); }
            catch (DecoderFallbackException exception) { throw new FormatException("invalid URI UTF-8", exception); }
            for (var j = 0; j < text.Length; j++)
            {
                if (char.IsControl(text[j]) || char.IsWhiteSpace(text[j]) || text[j] == '\\')
                    throw new FormatException("invalid URI character");
                if (text[j] != '%') continue;
                if (j + 2 >= text.Length || !char.IsAsciiHexDigit(text[j + 1]) || !char.IsAsciiHexDigit(text[j + 2]))
                    throw new FormatException("invalid URI escape");
                j += 2;
            }
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "ipfs") ||
                !text.StartsWith(uri.Scheme + "://", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(uri.Host))
                throw new FormatException("expected an absolute HTTP, HTTPS or IPFS URI");
            locations[i] = new MediaLocation(role, text);
            previousRole = role;
        }
        reader.EnsureEnd();
        return locations;
    }
}
