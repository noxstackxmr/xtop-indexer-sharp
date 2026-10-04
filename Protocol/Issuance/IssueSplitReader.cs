using IndexerCore.Protocol.Collections;
using IndexerCore.Protocol.Messages;

namespace IndexerCore.Protocol.Issuance;

public sealed record IssuanceState(byte[] CollectionId, uint FirstSerial, uint Count, NewBinding Binding, byte[] PublicKey);
public sealed record IssuanceChild(uint FirstSerial, uint Count, NewBinding Binding, byte Kind);
public sealed record IssueSplit(byte[] CollectionId, byte[] ParentKeyImage, IssuanceChild[] Children);

public static class IssueSplitReader
{
    public static ushort ReadProfile(XtopMessage message)
    {
        if (message.Operation != 0x12) throw new FormatException("expected ISSUE_SPLIT");
        if (message.Witnesses.Length == 0) throw new FormatException("split witnesses are required");
        var profile = message.Witnesses[0].Profile;
        if (profile != IssueSplitProofs.Profile && profile != CompactIssueSplitProofs.Profile)
            throw new NotSupportedException("split proof profile is not supported");
        if (message.Witnesses.Any(w => w.Profile != profile))
            throw new FormatException("mixed split proof profiles");
        return profile;
    }

    public static (byte[] CollectionId, byte[] ParentKeyImage) ReadReference(XtopMessage message)
    {
        if (ReadProfile(message) == CompactIssueSplitProofs.Profile)
        {
            var compact = CompactIssueSplitProofs.Read(message);
            return (compact.CollectionId, compact.ParentKeyImage);
        }
        var split = Read(message);
        return (split.CollectionId, split.ParentKeyImage);
    }

    public static IssueSplit Read(XtopMessage message)
    {
        if (message.Operation != 0x12) throw new FormatException("expected ISSUE_SPLIT");
        var reader = new PayloadReader(message.Payload);
        var id = reader.Take(32).ToArray();
        var parent = reader.Take(32).ToArray();
        var count = reader.ReadByte();
        if (count is < 2 or > 3) throw new FormatException("SPLIT_V1 requires two or three children");
        var children = new IssuanceChild[count];
        for (var i = 0; i < count; i++)
        {
            var first = reader.ReadUInt32();
            var length = reader.ReadUInt32();
            var binding = new NewBinding(reader.ReadByte(), reader.Take(32).ToArray(), reader.Take(32).ToArray(),
                reader.ReadUInt64(), reader.ReadByte(), reader.ReadByte());
            children[i] = new(first, length, binding, reader.ReadByte());
        }
        reader.EnsureEnd();
        return new(id, parent, children);
    }
}
