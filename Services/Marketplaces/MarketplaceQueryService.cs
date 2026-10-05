using System.Data;
using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Models.Marketplaces;
using IndexerCore.Monero;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static IndexerCore.Services.Items.ItemReadData;

namespace IndexerCore.Services.Marketplaces;

public sealed class MarketplaceQueryService(IndexerDbContext db, IOptions<MoneroOptions> options)
{
    private IQueryable<MarketplaceRevision> Revisions() => db.MarketplaceRevisions.AsNoTracking()
        .Where(r => r.Marketplace.Network == options.Value.XtopNetwork)
        .Include(r => r.Message).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
        .Include(r => r.Marketplace).ThenInclude(m => m.RegistrationMessage).ThenInclude(m => m.Transaction).ThenInclude(t => t.Block)
        .Include(r => r.Previous);

    public async Task<MarketplaceListResponse> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var offset = Offset(page, pageSize);
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var tip = await Tip(db, options.Value.XtopNetwork, false, cancellationToken);
        var query = Revisions().Where(r => !db.MarketplaceRevisions.Any(next => next.MarketplaceId == r.MarketplaceId && next.Revision > r.Revision));
        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderBy(r => r.Marketplace.RegistrationMessage.Transaction.Block.Height)
            .ThenBy(r => r.Marketplace.RegistrationMessage.Transaction.Position).ThenBy(r => r.Marketplace.RegistrationMessageId)
            .Skip(offset).Take(pageSize).ToArrayAsync(cancellationToken);
        await snapshot.CommitAsync(cancellationToken);
        return new(options.Value.Network, options.Value.XtopNetwork, tip, page, pageSize, total, rows.Select(Response).ToArray());
    }

    public async Task<MarketplaceDetailsResponse?> GetAsync(byte[] id, byte[]? configHash, CancellationToken cancellationToken)
    {
        if (id.Length != 32 || configHash is { Length: not 32 }) throw new ArgumentException("invalid marketplace identity");
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var query = Revisions().Where(r => r.Marketplace.ProtocolId == id);
        if (configHash != null) query = query.Where(r => r.ConfigHash == configHash);
        var row = await query.OrderByDescending(r => r.Revision).FirstOrDefaultAsync(cancellationToken);
        if (row == null) return null;
        var tip = await Tip(db, options.Value.XtopNetwork, false, cancellationToken);
        await snapshot.CommitAsync(cancellationToken);
        return new(options.Value.Network, options.Value.XtopNetwork, tip, Response(row));
    }

    private static MarketplaceResponse Response(MarketplaceRevision r)
    {
        List<string> modes = [];
        if ((r.Modes & 1) != 0) modes.Add("marketplace_managed");
        if ((r.Modes & 2) != 0) modes.Add("self_managed");
        return new(Hex(r.Marketplace.ProtocolId), Hex(r.Marketplace.ManagementKey), Hex(r.ConfigHash), 1, r.Revision,
            r.Previous == null ? null : Hex(r.Previous.ConfigHash), r.Name, r.WebsiteUrl, r.CommunicationUrl, r.ApiVersion,
            modes.ToArray(), Atomic(r.CreationFee), r.PrimaryFeeBps, r.SecondaryFeeBps, Payout(r.FeeAddress),
            (r.Modes & 1) != 0 ? Payout(r.CustodyAddress) : null,
            Transaction(r.Marketplace.RegistrationMessage.Transaction), Transaction(r.Message.Transaction));
    }
}
