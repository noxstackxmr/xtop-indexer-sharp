using System.Text;
using System.Text.Json;

namespace IndexerCore.Monero;

public sealed record MoneroChainInfo(ulong Height, string Network);
public sealed record MoneroBlock(ulong Height, string Hash, string PreviousHash, DateTimeOffset Timestamp, string[] TransactionIds);
public sealed record MoneroTransaction(string Id, byte[] Extra, byte[] NativeData);

public sealed class MoneroRpcClient(HttpClient http)
{
    public async Task<MoneroChainInfo> GetInfoAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync("get_info", cancellationToken);
        using var document = await ReadResponseAsync(response, cancellationToken);
        var result = document.RootElement;
        CheckStatus(result);
        return new MoneroChainInfo(result.GetProperty("height").GetUInt64(), result.GetProperty("nettype").GetString()!);
    }

    public async Task<MoneroBlock> GetBlockAsync(ulong height, CancellationToken cancellationToken)
    {
        using var response = await PostAsync("json_rpc", new
        {
            jsonrpc = "2.0", id = "0", method = "get_block", @params = new { height }
        }, cancellationToken);
        using var document = await ReadResponseAsync(response, cancellationToken);
        if (document.RootElement.TryGetProperty("error", out var error))
            throw new InvalidDataException($"Monero get_block failed: {error}");

        var result = document.RootElement.GetProperty("result");
        CheckStatus(result);
        var header = result.GetProperty("block_header");
        if (header.GetProperty("height").GetUInt64() != height || header.GetProperty("orphan_status").GetBoolean())
            throw new InvalidDataException("The daemon returned an unexpected block.");

        var ids = result.TryGetProperty("tx_hashes", out var hashes)
            ? hashes.EnumerateArray().Select(h => h.GetString()!).ToArray() : [];
        return new MoneroBlock(height, header.GetProperty("hash").GetString()!, header.GetProperty("prev_hash").GetString()!,
            DateTimeOffset.FromUnixTimeSeconds(header.GetProperty("timestamp").GetInt64()), ids);
    }

    public async Task<MoneroTransaction[]> GetTransactionsAsync(MoneroBlock block, CancellationToken cancellationToken)
    {
        var transactions = new Dictionary<string, MoneroTransaction>(StringComparer.Ordinal);
        foreach (var batch in block.TransactionIds.Chunk(100))
        {
            using var response = await PostAsync("get_transactions", new
            {
                txs_hashes = batch, decode_as_json = true, prune = true
            }, cancellationToken);
            using var document = await ReadResponseAsync(response, cancellationToken);
            var result = document.RootElement;
            CheckStatus(result);
            if (result.TryGetProperty("missed_tx", out var missed) && missed.GetArrayLength() != 0)
                throw new InvalidDataException("The daemon is missing transactions from the requested block.");

            foreach (var entry in result.GetProperty("txs").EnumerateArray())
            {
                var id = entry.GetProperty("tx_hash").GetString()!;
                if (!batch.Contains(id, StringComparer.Ordinal) || entry.GetProperty("in_pool").GetBoolean() ||
                    entry.GetProperty("block_height").GetUInt64() != block.Height)
                    throw new InvalidDataException("A returned transaction does not belong to the requested block.");

                using var decoded = JsonDocument.Parse(entry.GetProperty("as_json").GetString()!);
                var extra = decoded.RootElement.GetProperty("extra").EnumerateArray().Select(b => b.GetByte()).ToArray();
                var hex = entry.TryGetProperty("pruned_as_hex", out var pruned) ? pruned.GetString() : null;
                if (string.IsNullOrEmpty(hex)) hex = entry.GetProperty("as_hex").GetString();
                if (string.IsNullOrEmpty(hex)) throw new InvalidDataException("missing native transaction bytes");
                var native = Convert.FromHexString(hex);
                if (!transactions.TryAdd(id, new MoneroTransaction(id, extra, native)))
                    throw new InvalidDataException("The daemon returned a duplicate transaction.");
            }
        }

        return transactions.Count != block.TransactionIds.Length 
            ? throw new InvalidDataException("The daemon returned an incomplete block transaction list.") :
            [.. block.TransactionIds.Select(id => transactions[id])];
    }

    private async Task<HttpResponseMessage> PostAsync(string route, object request, CancellationToken cancellationToken)
    {
        using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        return await http.PostAsync(route, content, cancellationToken);
    }

    private static async Task<JsonDocument> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static void CheckStatus(JsonElement result)
    {
        if (result.GetProperty("status").GetString() != "OK")
            throw new InvalidDataException("Monero RPC did not return status OK.");
        if (result.TryGetProperty("untrusted", out var untrusted) && untrusted.GetBoolean())
            throw new InvalidDataException("A locally synchronized daemon is required; bootstrap RPC results are not accepted.");
    }
}
