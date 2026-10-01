using IndexerCore.Monero;
using IndexerCore.Protocol;
using Microsoft.Extensions.Options;

namespace IndexerCore.Services;

public sealed class TransactionScanner(
    MoneroRpcClient rpc,
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

        var nextHeight = settings.StartHeight;
        MoneroBlock? lastBlock = null;
        logger.LogInformation("Starting read-only scanning at height {Height}. The cursor is kept in memory.", nextHeight);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var info = await rpc.GetInfoAsync(stoppingToken);
                if (info.Network != settings.Network)
                    throw new InvalidDataException($"expected network {settings.Network}, got {info.Network}");

                // unavailable or lagging node is not evidence of a reorganization
                if (lastBlock != null && info.Height <= lastBlock.Height)
                    throw new InvalidDataException("The daemon is behind the last scanned block; waiting.");
                if (lastBlock != null &&
                    (await rpc.GetBlockAsync(lastBlock.Height, stoppingToken)).Hash != lastBlock.Hash)
                    RestartScan();

                var firstHeight = nextHeight;
                for (var scanned = 0; scanned < 100 && nextHeight < info.Height; scanned++)
                {
                    var block = await rpc.GetBlockAsync(nextHeight, stoppingToken);
                    if (lastBlock != null && block.PreviousHash != lastBlock.Hash)
                    {
                        RestartScan();
                        break;
                    }
                    var transactions = await rpc.GetTransactionsAsync(block, stoppingToken);
                    if ((await rpc.GetBlockAsync(nextHeight, stoppingToken)).Hash != block.Hash)
                    {
                        RestartScan();
                        break;
                    }

                    foreach (var transaction in transactions)
                    {
                        try
                        {
                            var message = XtopMessageReader.ReadExtra(transaction.Extra, settings.XtopNetwork);
                            if (message == null) continue;
                            logger.LogInformation(
                                "XTOP envelope at block {Height}, tx {TransactionId}: op 0x{Operation:X2}, payload {PayloadBytes} bytes, witnesses {WitnessCount}. Proofs are not verified yet.",
                                block.Height, transaction.Id, message.Operation, message.Payload.Length, message.Witnesses.Length);
                        }
                        catch (FormatException exception)
                        {
                            logger.LogWarning("Skipping unsupported or malformed extra in tx {TransactionId}: {Reason}",
                                transaction.Id, exception.Message);
                        }
                    }

                    lastBlock = block;
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
                logger.LogWarning(exception, "Scanning failed at height {Height}; the block will be retried.", nextHeight);
            }

            await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), stoppingToken);
        }

        return;

        void RestartScan()
        {
            logger.LogWarning("Chain history changed. Restarting the in-memory scan at height {Height}.", settings.StartHeight);
            nextHeight = settings.StartHeight;
            lastBlock = null;
        }
    }
}
