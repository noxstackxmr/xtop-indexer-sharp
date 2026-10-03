using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Monero;
using IndexerCore.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IndexerCore.Services;

public sealed class MessageProcessor(
    IServiceScopeFactory scopeFactory,
    SemaphoreSlim stateLock,
    IOptions<MoneroOptions> options,
    ILogger<MessageProcessor> logger) : BackgroundService
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
                    var db = scope.ServiceProvider.GetRequiredService<IndexerDbContext>();
                    var messages = await db.Messages
                        .Where(m => m.Status == MessageStatus.Pending && m.Transaction.Block.Network == settings.XtopNetwork)
                        .OrderBy(m => m.Transaction.Block.Height)
                        .ThenBy(m => m.Transaction.Position)
                        .Take(100)
                        .ToListAsync(stoppingToken);

                    foreach (var message in messages)
                    {
                        try
                        {
                            XtopMessageReader.ReadMessage(message.Data, settings.XtopNetwork);
                            message.Status = MessageStatus.Parsed;
                            message.Error = null;
                        }
                        catch (NotSupportedException exception)
                        {
                            message.Status = MessageStatus.Unsupported;
                            message.Error = exception.Message;
                        }
                        catch (FormatException exception)
                        {
                            message.Status = MessageStatus.Invalid;
                            message.Error = exception.Message;
                        }
                    }

                    if (messages.Count > 0)
                    {
                        await db.SaveChangesAsync(stoppingToken);
                        logger.LogInformation("checked envelopes for {Count} saved messages", messages.Count);
                    }
                    if (messages.Count == 100) continue;
                }
                finally
                {
                    stateLock.Release();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "message processing failed; pending messages will be retried");
            }

            await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), stoppingToken);
        }
    }
}
