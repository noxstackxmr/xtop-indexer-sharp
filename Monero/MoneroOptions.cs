using System.ComponentModel.DataAnnotations;

namespace IndexerCore.Monero;

public sealed class MoneroOptions
{
    public const string SectionName = "Monero";

    public bool Enabled { get; set; } = true;

    [Required]
    public string RpcUrl { get; set; } = "http://127.0.0.1:18081/";

    [Required]
    [RegularExpression("^(mainnet|testnet|stagenet|fakechain)$")]
    public string Network { get; set; } = "fakechain";

    public byte XtopNetwork => Network switch
    {
        "mainnet" => 0,
        "testnet" => 1,
        "stagenet" => 2,
        "fakechain" => 255,
        _ => throw new InvalidOperationException($"unsupported network {Network}")
    };

    public ulong StartHeight { get; set; }

    [Range(1, 3600)]
    public int PollIntervalSeconds { get; set; } = 5;

    [Range(1, 300)]
    public int RequestTimeoutSeconds { get; set; } = 15;
}
