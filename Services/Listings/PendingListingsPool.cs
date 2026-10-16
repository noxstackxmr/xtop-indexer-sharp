using System.Security.Cryptography;
using IndexerCore.Data;
using IndexerCore.Monero;
using IndexerCore.Protocol.Messages;
using IndexerCore.Protocol.Sales;
using IndexerCore.Services.Items;
using Microsoft.Extensions.Options;

namespace IndexerCore.Services.Listings;

public sealed record PendingPoolSnapshot(DateTimeOffset? UpdatedAt, bool Available, bool Truncated, string? CheckedTip, PendingListing[] Listings);

public sealed class PendingListingsPool(MoneroRpcClient rpc, IServiceScopeFactory scopes, IOptions<MoneroOptions> settings,
    IConfiguration configuration, ILogger<PendingListingsPool> logger, TimeProvider clock) : BackgroundService
{
    private readonly Dictionary<string, Candidate?> tracked = new();
    private PendingPoolSnapshot snapshot = new(null, false, false, null, []);
    public PendingPoolSnapshot Snapshot()
    {
        var current = Volatile.Read(ref snapshot);
        return current.UpdatedAt < clock.GetUtcNow().AddSeconds(-30)
            ? current with { Available = false, Listings = [] } : current;
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        if (!settings.Value.Enabled || !configuration.GetValue<bool>("PendingListings:Enabled")) return;
        while (!token.IsCancellationRequested)
        {
            try { await RefreshAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogWarning(e, "pending listing pool refresh failed; retaining last short-lived snapshot"); }
            await Task.Delay(TimeSpan.FromSeconds(5), token);
        }
    }

    public async Task RefreshAsync(CancellationToken token)
    {
        var info = await rpc.GetInfoAsync(token);
        if (info.Network != settings.Value.Network) throw new InvalidDataException("incorrect mempool network");
        var hashes = (await rpc.GetPoolHashesAsync(token)).ToHashSet(StringComparer.Ordinal);
        foreach (var id in tracked.Keys.Where(id => !hashes.Contains(id)).ToArray()) tracked.Remove(id);
        var unseen = hashes.Where(id => !tracked.ContainsKey(id)).Take(200).ToArray();
        foreach (var batch in unseen.Chunk(20))
        {
            var transactions = await rpc.GetPoolTransactionsAsync(batch, token);
            foreach (var id in batch) tracked.TryAdd(id, null);
            foreach (var transaction in transactions)
            {
                try
                {
                    var data = XtopMessageReader.ExtractMessage(transaction.Extra);
                    if (data == null || !ListingModes.IsListing(XtopMessageReader.ReadOperation(data))) continue;
                    if (tracked.Values.Count(value => value != null) >= 4096) { tracked.Remove(transaction.Id); continue; }
                    tracked[transaction.Id] = new Candidate(transaction, clock.GetUtcNow());
                }
                catch (FormatException) { }
            }
        }
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IndexerDbContext>();
        var tip = await ItemReadData.Tip(db, settings.Value.XtopNetwork, true, token);
        var scanned = await ItemReadData.Tip(db, settings.Value.XtopNetwork, false, token);
        if (tip == null || tip != scanned || checked((ulong)tip.Height + 1) != info.Height)
            throw new InvalidOperationException("indexer is still checking the chain");
        var query = scope.ServiceProvider.GetRequiredService<PendingListingQuery>();
        foreach (var candidate in tracked.Values.Where(c => c != null && c.CheckedTip != tip.Hash).Take(200))
        {
            try { candidate!.Listing = await query.ValidateAsync(candidate.Transaction, checked(tip.Height + 1), token); }
            catch (Exception e) when (e is FormatException or CryptographicException or NotSupportedException or OverflowException)
            { candidate!.Listing = null; }
            finally { db.ChangeTracker.Clear(); }
            candidate!.CheckedTip = tip.Hash;
        }
        var stillPooled = (await rpc.GetPoolHashesAsync(token)).ToHashSet(StringComparer.Ordinal);
        var listings = tracked.Where(p => stillPooled.Contains(p.Key) && p.Value?.CheckedTip == tip.Hash && p.Value.Listing != null)
            .Select(p => p.Value!.Listing! with { FirstSeenAt = p.Value.FirstSeenAt }).OrderBy(l => l.TransactionId, StringComparer.Ordinal).ToArray();
        var incomplete = hashes.Any(id => !tracked.ContainsKey(id)) || tracked.Values.Any(c => c != null && c.CheckedTip != tip.Hash);
        Volatile.Write(ref snapshot, new(clock.GetUtcNow(), true, incomplete, tip.Hash, listings));
    }

    private sealed class Candidate(MoneroTransaction transaction, DateTimeOffset firstSeenAt)
    {
        public DateTimeOffset FirstSeenAt { get; } = firstSeenAt;
        public MoneroTransaction Transaction { get; } = transaction;
        public string? CheckedTip { get; set; }
        public PendingListing? Listing { get; set; }
    }
}
