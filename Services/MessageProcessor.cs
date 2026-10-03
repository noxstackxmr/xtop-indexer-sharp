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
                    var chunks = scope.ServiceProvider.GetRequiredService<DataChunkHandler>();
                    var attachments = scope.ServiceProvider.GetRequiredService<AttachmentService>();
                    var affected = new HashSet<Attachment>();
                    var messages = await db.Messages
                        .Where(m => (m.Status == MessageStatus.Pending ||
                                     (m.Status == MessageStatus.Unsupported && m.Version == XtopMessageReader.CurrentVersion) ||
                                     (m.Status == MessageStatus.Parsed && m.Operation == 0x01)) &&
                                    m.Transaction.Block.Network == settings.XtopNetwork)
                        .OrderBy(m => m.Transaction.Block.Height)
                        .ThenBy(m => m.Transaction.Position)
                        .Take(100)
                        .ToListAsync(stoppingToken);

                    foreach (var message in messages)
                    {
                        try
                        {
                            var envelope = XtopMessageReader.ReadMessage(message.Data, settings.XtopNetwork);
                            message.Operation = envelope.Operation;
                            if (envelope.Operation == 0x01)
                            {
                                affected.Add(await chunks.HandleAsync(message, envelope, stoppingToken));
                                message.Status = MessageStatus.Valid;
                            }
                            else
                            {
                                message.Status = MessageStatus.Parsed;
                            }
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

                    var ready = await attachments.GetReadyAsync(settings.XtopNetwork, stoppingToken);
                    affected.UnionWith(ready);
                    foreach (var attachment in affected.Where(a => a.Status == AttachmentStatus.Incomplete))
                        await attachments.RefreshAsync(attachment, stoppingToken);

                    if (db.ChangeTracker.HasChanges())
                    {
                        await db.SaveChangesAsync(stoppingToken);
                        logger.LogInformation("processed {Count} saved messages and checked {Attachments} attachments", messages.Count, affected.Count);
                    }
                    if (messages.Count == 100 || ready.Count == 100) continue;
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
