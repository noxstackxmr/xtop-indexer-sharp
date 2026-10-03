using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Monero;
using IndexerCore.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IndexerCore.Services;

public sealed class TransactionScanner(
    MoneroRpcClient rpc,
    IServiceScopeFactory scopeFactory,
    IOptions<MoneroOptions> options,
    ILogger<TransactionScanner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Transaction scanning is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<IndexerDbContext>();
                var lastBlock = await db.Blocks.AsNoTracking()
                    .Where(b => b.Network == settings.XtopNetwork)
                    .OrderByDescending(b => b.Height)
                    .FirstOrDefaultAsync(stoppingToken);
                var nextHeight = lastBlock == null ? settings.StartHeight : checked((ulong)lastBlock.Height + 1);
                var info = await rpc.GetInfoAsync(stoppingToken);
                if (info.Network != settings.Network)
                    throw new InvalidDataException($"expected network {settings.Network}, got {info.Network}");

                // unavailable or lagging node is not evidence of a reorganization
                if (lastBlock != null && info.Height <= (ulong)lastBlock.Height)
                    throw new InvalidDataException("The daemon is behind the last scanned block; waiting.");
                if (lastBlock != null)
                {
                    var currentTip = await rpc.GetBlockAsync((ulong)lastBlock.Height, stoppingToken);
                    if (!Convert.FromHexString(currentTip.Hash).AsSpan().SequenceEqual(lastBlock.Hash))
                        throw new InvalidDataException("stored chain changed; database rollback is required before scanning can continue");
                }

                var firstHeight = nextHeight;
                for (var scanned = 0; scanned < 100 && nextHeight < info.Height; scanned++)
                {
                    var block = await rpc.GetBlockAsync(nextHeight, stoppingToken);
                    if (lastBlock != null && !Convert.FromHexString(block.PreviousHash).AsSpan().SequenceEqual(lastBlock.Hash))
                        throw new InvalidDataException("block does not extend the stored chain");
                    var transactions = await rpc.GetTransactionsAsync(block, stoppingToken);
                    if ((await rpc.GetBlockAsync(nextHeight, stoppingToken)).Hash != block.Hash)
                        throw new InvalidDataException("block changed while its transactions were being read");

                    var storedBlock = new Block
                    {
                        Network = settings.XtopNetwork,
                        Height = checked((long)block.Height),
                        Hash = Convert.FromHexString(block.Hash),
                        PreviousHash = Convert.FromHexString(block.PreviousHash),
                        Timestamp = block.Timestamp
                    };
                    for (var position = 0; position < transactions.Length; position++)
                    {
                        var transaction = transactions[position];
                        try
                        {
                            var data = XtopMessageReader.ExtractMessage(transaction.Extra);
                            if (data == null) continue;
                            storedBlock.Transactions.Add(new Transaction
                            {
                                Hash = Convert.FromHexString(transaction.Id),
                                Position = position,
                                Message = new Message
                                {
                                    Data = data,
                                    Version = data.Length > 4 ? data[4] : (byte)0,
                                    Network = data.Length > 5 ? data[5] : (byte)0,
                                    Operation = data.Length > 6 && data[4] == 1 ? data[6] : (byte)0
                                }
                            });
                        }
                        catch (FormatException exception)
                        {
                            logger.LogWarning("Skipping unsupported or malformed extra in tx {TransactionId}: {Reason}",
                                transaction.Id, exception.Message);
                        }
                    }

                    db.Blocks.Add(storedBlock);
                    await db.SaveChangesAsync(stoppingToken);
                    db.ChangeTracker.Clear();
                    lastBlock = storedBlock;
                    nextHeight = checked(block.Height + 1);
                }
                if (nextHeight > firstHeight)
                    logger.LogInformation("Scanned blocks {FirstHeight}..{LastHeight}; daemon height {ChainHeight}.",
                        firstHeight, nextHeight - 1, info.Height);
                if (nextHeight < info.Height) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "scanning failed; retrying from the last saved block");
            }

            await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), stoppingToken);
        }
    }
}
