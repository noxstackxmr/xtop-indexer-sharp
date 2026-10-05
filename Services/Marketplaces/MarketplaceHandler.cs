using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Protocol.Marketplaces;
using IndexerCore.Protocol.Messages;
using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Services.Marketplaces;

public sealed class MarketplaceHandler(IndexerDbContext db)
{
    public async Task HandleAsync(Message message, XtopMessage envelope, CancellationToken cancellationToken)
    {
        var c = MarketplaceFormat.Read(envelope, message.Network);
        if (await db.MarketplaceRevisions.AnyAsync(r => r.MessageId == message.TransactionId, cancellationToken)) return;
        var id = MarketplaceFormat.Identity(c.Network, c.ManagementKey);
        var market = await db.Marketplaces.SingleOrDefaultAsync(m => m.Network == c.Network && m.ProtocolId == id, cancellationToken);
        MarketplaceRevision? previous = null;
        if (c.Revision == 0)
        {
            if (market != null) throw new FormatException("marketplace is already registered");
        }
        else
        {
            if (market == null) throw new FormatException("missing marketplace registration");
            previous = await db.MarketplaceRevisions.Include(r => r.Message).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
                .Where(r => r.MarketplaceId == market.Id).OrderByDescending(r => r.Revision).FirstAsync(cancellationToken);
            if (previous.Revision + 1 != c.Revision || !previous.ConfigHash.AsSpan().SequenceEqual(c.PreviousHash))
                throw new FormatException("marketplace revision must extend the current revision");
            var tx = previous.Message.Transaction; var current = message.Transaction;
            if (tx.Block.Height > current.Block.Height || (tx.Block.Height == current.Block.Height && tx.Position >= current.Position))
                throw new FormatException("marketplace revision must follow its predecessor");
        }
        market ??= new Marketplace { Network = c.Network, ProtocolId = id, ManagementKey = c.ManagementKey,
            RegistrationMessageId = message.TransactionId, RegistrationMessage = message };
        db.MarketplaceRevisions.Add(new MarketplaceRevision
        {
            MessageId = message.TransactionId, Message = message, Marketplace = market, Revision = c.Revision,
            ConfigHash = envelope.ConfigHash, Previous = previous, Name = c.Name, WebsiteUrl = c.WebsiteUrl,
            CommunicationUrl = c.CommunicationUrl, ApiVersion = c.ApiVersion, Modes = c.Modes,
            CreationFee = c.CreationFee, PrimaryFeeBps = c.PrimaryFeeBps, SecondaryFeeBps = c.SecondaryFeeBps,
            FeeAddress = c.FeeAddress, CustodyAddress = c.CustodyAddress
        });
    }
}
