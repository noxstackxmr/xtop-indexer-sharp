using IndexerCore.Data;
using IndexerCore.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IndexerCore.Services;

public sealed class ProtocolConfigurationRegistry
{
    private readonly Dictionary<string, (long Height, CollectionCreatePolicy Policy)> configurations = [];

    public ProtocolConfigurationRegistry(IOptions<ProtocolOptions> options)
    {
        if ((from configuration in options.Value.Configurations let policy = configuration.BuildPolicy() where !configurations.TryAdd(Convert.ToHexString(policy.ConfigHash), (configuration.ActivationHeight, policy)) select configuration).Any())
        {
            throw new FormatException("duplicate protocol configuration");
        }
    }

    public CollectionCreatePolicy Resolve(byte network, byte[] hash, long height)
    {
        if (!configurations.TryGetValue(Convert.ToHexString(hash), out var configuration) || configuration.Policy.Network != network)
            throw new NotSupportedException("protocol configuration is not allowed");
        return height < configuration.Height ? throw new FormatException("protocol configuration is not active at this height") : configuration.Policy;
    }

    public async Task ValidateHistoryAsync(IndexerDbContext db, CancellationToken cancellationToken)
    {
        var used = await db.Collections.Select(c => new { c.Network, c.ConfigHash, Height = c.CreationMessage.Transaction.Block.Height })
            .Distinct().ToListAsync(cancellationToken);
        foreach (var configuration in used)
            Resolve(configuration.Network, configuration.ConfigHash, configuration.Height);
    }
}
