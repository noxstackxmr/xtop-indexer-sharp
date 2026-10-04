using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Protocol.Collections;
using IndexerCore.Protocol.Issuance;
using IndexerCore.Protocol.Messages;
using IndexerCore.Services.Indexing;
using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Services.Issuance;

public sealed class IssueSplitHandler(IndexerDbContext db, ProtocolConfigurationRegistry configurations, NativeTransactionReader transactions)
{
    public async Task HandleAsync(Message message, XtopMessage envelope, CancellationToken cancellationToken)
    {
        var transaction = message.Transaction;
        var block = transaction.Block;
        var policy = configurations.Resolve(block.Network, envelope.ConfigHash, block.Height);
        var reference = IssueSplitReader.ReadReference(envelope);
        var parent = await db.CollectionOutputs.Include(o => o.Collection).Include(o => o.Split)
            .Include(o => o.SourceMessage).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
            .SingleOrDefaultAsync(o => o.Network == block.Network && o.KeyImage == reference.ParentKeyImage, cancellationToken)
            ?? throw new FormatException("issuance parent does not exist");
        if (!parent.Collection.ProtocolId.AsSpan().SequenceEqual(reference.CollectionId) ||
            !parent.Collection.ConfigHash.AsSpan().SequenceEqual(policy.ConfigHash))
            throw new FormatException("issuance collection or configuration mismatch");
        if (parent.Kind != CollectionOutputKind.Issuance || parent.RangeStart == null || parent.RangeEnd == null ||
            parent.RangeStart < 0 || parent.RangeEnd > parent.Collection.MaxSupply || parent.RangeEnd - parent.RangeStart < 2)
            throw new FormatException("parent must be an issuance range");
        var origin = parent.SourceMessage.Transaction;
        if (origin.Block.Height > block.Height || (origin.Block.Height == block.Height && origin.Position >= transaction.Position))
            throw new FormatException("issuance parent must precede its split");
        if (parent.Split != null && parent.Split.MessageId != message.TransactionId)
            throw new FormatException("issuance parent has already been split");
        var native = await transactions.ReadAsync(message, cancellationToken);
        var state = new IssuanceState(parent.Collection.ProtocolId, checked((uint)parent.RangeStart),
            checked((uint)(parent.RangeEnd - parent.RangeStart)),
            new NewBinding(parent.OutputIndex, parent.KeyImage, parent.OwnerKey, checked((ulong)parent.NominalAmount), 0, 1), parent.PublicKey);
        var split = IssueSplitProofs.Verify(transaction.NativeData!, policy, state);
        if (await db.IssuanceSplits.AnyAsync(s => s.MessageId == message.TransactionId, cancellationToken)) return;
        foreach (var child in split.Children)
            if (await db.CollectionOutputs.AnyAsync(o => o.Network == block.Network && o.KeyImage == child.Binding.KeyImage, cancellationToken) ||
                await db.CollectionChanges.AnyAsync(c => c.Network == block.Network && c.KeyImage == child.Binding.KeyImage, cancellationToken))
                throw new FormatException("child key image is already bound");
        foreach (var image in native.InputKeyImages.Where(i => !i.AsSpan().SequenceEqual(parent.KeyImage)))
            if (await db.CollectionOutputs.AnyAsync(o => o.Network == block.Network && o.KeyImage == image, cancellationToken) ||
                await db.CollectionChanges.AnyAsync(c => c.Network == block.Network && c.KeyImage == image, cancellationToken))
                throw new FormatException("split must not spend another bound output");

        db.IssuanceSplits.Add(new IssuanceSplit { Message = message, ParentOutput = parent });
        foreach (var child in split.Children)
        {
            var binding = child.Binding;
            db.CollectionOutputs.Add(new CollectionOutput
            {
                Collection = parent.Collection, SourceMessage = message, Network = block.Network,
                Kind = child.Kind == 1 ? CollectionOutputKind.Item : CollectionOutputKind.Issuance,
                ItemId = child.Kind == 1 ? Protocol.Items.ItemIdentity.Derive(parent.Collection.ProtocolId, child.FirstSerial) : null,
                OutputIndex = binding.OutputIndex, PublicKey = native.Outputs[binding.OutputIndex].Key,
                KeyImage = binding.KeyImage, OwnerKey = binding.OwnerKey, NominalAmount = binding.NominalAmount,
                RangeStart = child.FirstSerial, RangeEnd = (long)child.FirstSerial + child.Count
            });
        }
    }
}
