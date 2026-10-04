using IndexerCore.Protocol.Attachments;

namespace IndexerCore.Protocol.Collections;

public sealed record CollectionMetadataState(byte Mode, bool Revealed, MediaLocation[] Locations)
{
    public string Status => Mode == 0 ? "open" : Revealed ? "revealed" : "unrevealed";

    public CollectionMetadataState Apply(byte operation, MediaLocation[] replacements)
    {
        if (operation == 0x0F)
        {
            if (replacements.Length != 0) throw new FormatException("control transfer cannot change locations");
            return this;
        }
        if (operation == 0x0D)
        {
            if (Mode != 1 || Revealed) throw new FormatException("collection cannot be revealed again or was created open");
            if (replacements.Length != 1 || replacements[0].Role != 2)
                throw new FormatException("REVEAL requires only the items location");
            return this with { Revealed = true, Locations = [Locations.Single(l => l.Role == 1), replacements[0]] };
        }
        if (operation != 0x0E) throw new FormatException("unexpected collection operation");
        var itemRole = Mode == 1 && !Revealed ? 3 : 2;
        if (replacements.Length == 0 || replacements.Any(l => l.Role != 1 && l.Role != itemRole))
            throw new FormatException("UPDATE role is not allowed in the current state");
        var merged = Locations.ToDictionary(l => l.Role);
        foreach (var replacement in replacements) merged[replacement.Role] = replacement;
        return this with { Locations = merged.Values.OrderBy(l => l.Role).ToArray() };
    }
}
