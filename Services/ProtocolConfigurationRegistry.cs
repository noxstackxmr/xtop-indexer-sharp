using IndexerCore.Protocol.Collections;
using IndexerCore.Data;
using IndexerCore.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using IndexerCore.Data.Entities;
using IndexerCore.Protocol.Marketplaces;
using IndexerCore.Protocol.Messages;

namespace IndexerCore.Services;

public sealed class ProtocolConfigurationRegistry
{
    private readonly Dictionary<string, (long Height, CollectionCreatePolicy Policy, ushort? PrimaryFeeBps, ushort? SecondaryFeeBps)> configurations = [];

    public ProtocolConfigurationRegistry(IOptions<ProtocolOptions> options)
    {
        if ((from configuration in options.Value.Configurations let policy = configuration.BuildPolicy() where !configurations.TryAdd(Convert.ToHexString(policy.ConfigHash), (configuration.ActivationHeight, policy, configuration.PrimaryFeeBps, configuration.SecondaryFeeBps)) select configuration).Any())
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
        var used = await db.Collections.Where(c => c.MarketplacePolicy == null).Select(c => new { c.Network, c.ConfigHash, Height = c.CreationMessage.Transaction.Block.Height })
            .Distinct().ToListAsync(cancellationToken);
        foreach (var configuration in used)
            Resolve(configuration.Network, configuration.ConfigHash, configuration.Height);
    }

    public CollectionCreatePolicy ResolveCreation(XtopMessage message, byte network, long height)
        => message.Version == MarketplaceFormat.WireVersion
            ? MarketplacePolicyReader.ReadCreation(message, network).BuildPolicy(network)
            : Resolve(network, message.ConfigHash, height);

    public CollectionCreatePolicy ResolveCollection(Collection collection, long height)
        => collection.MarketplacePolicy == null ? Resolve(collection.Network, collection.ConfigHash, height)
            : MarketplacePolicyReader.Read(collection.MarketplacePolicy, collection.Network, collection.ConfigHash, false).BuildPolicy(collection.Network);

    public ushort? FindPrimaryFee(Collection collection, long height)
        => collection.MarketplacePolicy == null ? FindPrimaryFee(collection.Network, collection.ConfigHash, height)
            : MarketplacePolicyReader.Read(collection.MarketplacePolicy, collection.Network, collection.ConfigHash, false).FeeBps;

    public CollectionCreatePolicy ResolveListing(ItemTrade listing, Collection collection, long height)
        => listing.MarketplacePolicy == null ? ResolveCollection(collection, height)
            : MarketplacePolicyReader.Read(listing.MarketplacePolicy, collection.Network, listing.MarketplaceConfigHash!, true).BuildPolicy(collection.Network);

    public ushort ResolvePrimaryFee(byte network, byte[] hash, long height)
        => FindPrimaryFee(network, hash, height) ?? throw new NotSupportedException("primary purchases require configuration V2");

    public ushort? FindPrimaryFee(byte network, byte[] hash, long height)
    {
        _ = Resolve(network, hash, height);
        return configurations[Convert.ToHexString(hash)].PrimaryFeeBps;
    }

    public ushort ResolveSecondaryFee(byte network, byte[] hash, long height)
    {
        _ = Resolve(network, hash, height);
        return configurations[Convert.ToHexString(hash)].SecondaryFeeBps
            ?? throw new NotSupportedException("secondary trades require configuration V3");
    }
}
