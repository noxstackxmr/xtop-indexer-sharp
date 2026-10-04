using IndexerCore.Monero;
using IndexerCore.Services.Items;
using Microsoft.Extensions.Options;

namespace IndexerCore.Services.Indexing;

public sealed class MessageProcessor(IServiceScopeFactory scopeFactory, SemaphoreSlim stateLock,
    IOptions<MoneroOptions> options, ILogger<MessageProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await stateLock.WaitAsync(stoppingToken);
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<MessageBatchProcessor>();
                    var count = await processor.ProcessAsync(settings.XtopNetwork, stoppingToken);
                    if (count > 0) logger.LogInformation("processed {Count} saved messages or attachments", count);
                    var spends = scope.ServiceProvider.GetRequiredService<ItemSpendProcessor>();
                    var blocks = await spends.ProcessAsync(settings.XtopNetwork, stoppingToken);
                    if (blocks > 0) logger.LogInformation("checked item spends in {Count} blocks", blocks);
                    if (count == 100 || blocks == ItemSpendProcessor.BatchSize) continue;
                }
                finally { stateLock.Release(); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "index processing failed; unfinished work will be retried");
            }
            await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), stoppingToken);
        }
    }
}
