using IndexerCore.Protocol.Attachments;
using IndexerCore.Protocol.Messages;

namespace IndexerCore.Protocol.Collections;

public sealed record CollectionControlState(byte[] CollectionId, NewBinding Binding, byte[] PublicKey);
public sealed record CollectionControl(byte[] CollectionId, byte[] PreviousKeyImage, NewBinding Successor, ChunkReference? Locations);

public static class CollectionControlReader
{
    public static CollectionControl Read(XtopMessage message)
    {
        if (message.Operation is not (0x0D or 0x0E or 0x0F)) throw new FormatException("expected a collection control operation");
        var reader = new PayloadReader(message.Payload);
        var id = reader.Take(32).ToArray();
        var image = reader.Take(32).ToArray();
        var successor = new NewBinding(reader.ReadByte(), reader.Take(32).ToArray(), reader.Take(32).ToArray(),
            reader.ReadUInt64(), reader.ReadByte(), reader.ReadByte());
        var locations = message.Operation == 0x0F ? null : reader.ReadChunkReference();
        reader.EnsureEnd();
        return new(id, image, successor, locations);
    }
}
