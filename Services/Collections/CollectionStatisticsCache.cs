using Microsoft.Extensions.Caching.Memory;

namespace IndexerCore.Services.Collections;

public sealed class CollectionStatisticsCache(IServiceScopeFactory scopes, IHostApplicationLifetime lifetime,
    ILogger<CollectionStatisticsCache> logger) : IDisposable
{
    readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 128 });
    readonly SemaphoreSlim refresh = new(1, 1);
    sealed record Entry(CollectionStatistics? Data, DateTimeOffset RefreshAfter);
    public async Task<CollectionStatistics?> Get(string id, string period, CancellationToken ct)
    {
        var key = id + ":" + period;
        if (cache.TryGetValue<Entry>(key, out var entry))
        {
            if (entry!.RefreshAfter <= DateTimeOffset.UtcNow && refresh.Wait(0)) _ = Refresh(key, id, period);
            return entry.Data is { } data ? data with { Stale = data.UpdatedAt < DateTimeOffset.UtcNow.AddSeconds(-30) } : null;
        }
        if (!await refresh.WaitAsync(TimeSpan.FromSeconds(2), ct)) throw new StatisticsBusyException();
        try
        {
            if (cache.TryGetValue<Entry>(key, out entry)) return entry!.Data;
            return await Load(key, id, period, ct);
        }
        finally { refresh.Release(); }
    }
    async Task Refresh(string key, string id, string period)
    {
        try { await Load(key, id, period, lifetime.ApplicationStopping); }
        catch (Exception e)
        {
            logger.LogWarning(e, "Collection statistics refresh failed for {CollectionId}", id);
            if (cache.TryGetValue<Entry>(key, out var old)) Store(key, old!.Data, 10);
        }
        finally { refresh.Release(); }
    }
    async Task<CollectionStatistics?> Load(string key, string id, string period, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.ApplicationStopping);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await using var scope = scopes.CreateAsyncScope();
        var data = await scope.ServiceProvider.GetRequiredService<CollectionStatisticsQuery>().Read(id, period, timeout.Token);
        Store(key, data, 30);
        return data;
    }
    void Store(string key, CollectionStatistics? data, int seconds) => cache.Set(key,
        new Entry(data, DateTimeOffset.UtcNow.AddSeconds(seconds)), new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
    public void Dispose() => cache.Dispose();
}
public sealed class StatisticsBusyException : Exception;
