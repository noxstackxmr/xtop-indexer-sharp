using System.Globalization;
using System.Text.Json;

namespace IndexerCore.Services.Marketplaces;

public sealed record XmrUsdQuote(decimal? Usd, DateTimeOffset? UpdatedAt, DateTimeOffset ServerTime, bool Stale, string Source = "Kraken");

public sealed class XmrUsdRate(HttpClient http, TimeProvider clock, ILogger<XmrUsdRate> logger) : BackgroundService
{
    private sealed record Cached(decimal Usd, DateTimeOffset At);
    private Cached? cached;

    public XmrUsdQuote Read()
    {
        var value = Volatile.Read(ref cached);
        var now = clock.GetUtcNow();
        var age = value == null ? TimeSpan.MaxValue : now - value.At;
        return new(age >= TimeSpan.FromHours(1) ? null : value?.Usd, value?.At, now, age >= TimeSpan.FromMinutes(10));
    }

    public async Task RefreshAsync(CancellationToken token)
    {
        using var response = await http.GetAsync("https://api.kraken.com/0/public/Ticker?pair=XMRUSD", HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(65536, token);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = json.RootElement;
        if (!root.TryGetProperty("error", out var errors) || errors.ValueKind != JsonValueKind.Array || errors.GetArrayLength() != 0 ||
            !root.TryGetProperty("result", out var result) || !result.TryGetProperty("XXMRZUSD", out var pair) ||
            !pair.TryGetProperty("c", out var close) || close.ValueKind != JsonValueKind.Array || close.GetArrayLength() < 1 ||
            close[0].ValueKind != JsonValueKind.String ||
            !decimal.TryParse(close[0].GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var usd) || usd <= 0 || usd > 1_000_000)
            throw new InvalidDataException("Invalid XMR/USD quote");
        Volatile.Write(ref cached, new(usd, clock.GetUtcNow()));
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var delay = TimeSpan.FromMinutes(5);
            try { await RefreshAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                logger.LogWarning("XMR/USD refresh unavailable ({Type}); keeping the bounded cached quote", e.GetType().Name);
                delay = TimeSpan.FromMinutes(1);
            }
            await Task.Delay(delay, token);
        }
    }
}
