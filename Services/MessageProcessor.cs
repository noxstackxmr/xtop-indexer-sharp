using IndexerCore.Monero;
using Microsoft.Extensions.Options;

namespace IndexerCore.Services;

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
                    if (count == 100) continue;
                }
                finally { stateLock.Release(); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "message processing failed; pending messages will be retried");
            }
            await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), stoppingToken);
        }
    }
}
