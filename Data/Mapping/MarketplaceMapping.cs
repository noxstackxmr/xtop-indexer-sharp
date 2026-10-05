using IndexerCore.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Data.Mapping;

internal static class MarketplaceMapping
{
    public static void Configure(ModelBuilder model)
    {
        var market = model.Entity<Marketplace>();
        market.HasIndex(m => new { m.Network, m.ProtocolId }).IsUnique();
        market.Property(m => m.ProtocolId).HasMaxLength(32);
        market.Property(m => m.ManagementKey).HasMaxLength(32);
        market.HasOne(m => m.RegistrationMessage).WithMany().HasForeignKey(m => m.RegistrationMessageId).OnDelete(DeleteBehavior.Cascade);
        var revision = model.Entity<MarketplaceRevision>();
        revision.HasKey(r => r.MessageId);
        revision.HasIndex(r => new { r.MarketplaceId, r.Revision }).IsUnique();
        revision.HasIndex(r => r.ConfigHash).IsUnique();
        revision.Property(r => r.ConfigHash).HasMaxLength(32);
        revision.Property(r => r.Name).HasMaxLength(64);
        revision.Property(r => r.WebsiteUrl).HasMaxLength(256);
        revision.Property(r => r.CommunicationUrl).HasMaxLength(256);
        revision.Property(r => r.FeeAddress).HasMaxLength(64);
        revision.Property(r => r.CustodyAddress).HasMaxLength(64);
        revision.Property(r => r.CreationFee).HasPrecision(20, 0);
        revision.HasOne(r => r.Marketplace).WithMany(m => m.Revisions).HasForeignKey(r => r.MarketplaceId).OnDelete(DeleteBehavior.Cascade);
        revision.HasOne(r => r.Message).WithMany().HasForeignKey(r => r.MessageId).OnDelete(DeleteBehavior.Cascade);
        revision.HasOne(r => r.Previous).WithMany().HasForeignKey(r => r.PreviousMessageId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Collection>().Property(c => c.MarketplaceId).HasMaxLength(32);
        model.Entity<Collection>().Property(c => c.MarketplacePolicy).HasMaxLength(234);
        model.Entity<ItemTrade>().Property(t => t.MarketplaceId).HasMaxLength(32);
        model.Entity<ItemTrade>().Property(t => t.MarketplaceConfigHash).HasMaxLength(32);
        model.Entity<ItemTrade>().Property(t => t.MarketplacePolicy).HasMaxLength(291);
    }
}
