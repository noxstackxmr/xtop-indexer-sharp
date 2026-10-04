using IndexerCore.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Data;

public sealed class IndexerDbContext(DbContextOptions<IndexerDbContext> options) : DbContext(options)
{
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<DataChunk> DataChunks => Set<DataChunk>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CollectionOutput> CollectionOutputs => Set<CollectionOutput>();
    public DbSet<CollectionChange> CollectionChanges => Set<CollectionChange>();
    public DbSet<IssuanceSplit> IssuanceSplits => Set<IssuanceSplit>();
    public DbSet<PrimaryPurchase> PrimaryPurchases => Set<PrimaryPurchase>();
    public DbSet<PrimaryPurchaseItem> PrimaryPurchaseItems => Set<PrimaryPurchaseItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var block = modelBuilder.Entity<Block>();
        block.HasIndex(b => new { b.Network, b.Height }).IsUnique();
        block.HasIndex(b => new { b.Network, b.Hash }).IsUnique();
        block.Property(b => b.Hash).HasMaxLength(32);
        block.Property(b => b.PreviousHash).HasMaxLength(32);

        var transaction = modelBuilder.Entity<Transaction>();
        transaction.HasIndex(t => t.Hash);
        transaction.HasIndex(t => new { t.BlockId, t.Position }).IsUnique();
        transaction.Property(t => t.Hash).HasMaxLength(32);
        transaction.HasOne(t => t.Block)
            .WithMany(b => b.Transactions)
            .HasForeignKey(t => t.BlockId)
            .OnDelete(DeleteBehavior.Cascade);

        var message = modelBuilder.Entity<Message>();
        message.HasKey(m => m.TransactionId);
        message.Property(m => m.Status).HasConversion<byte>();
        message.HasOne(m => m.Transaction)
            .WithOne(t => t.Message)
            .HasForeignKey<Message>(m => m.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        var attachment = modelBuilder.Entity<Attachment>();
        attachment.HasIndex(a => new { a.Network, a.Hash, a.TotalLength, a.MerkleRoot }).IsUnique();
        attachment.Property(a => a.Hash).HasMaxLength(32);
        attachment.Property(a => a.MerkleRoot).HasMaxLength(32);
        attachment.Property(a => a.Status).HasConversion<byte>();

        var chunk = modelBuilder.Entity<DataChunk>();
        chunk.HasKey(c => c.MessageId);
        chunk.HasIndex(c => new { c.AttachmentId, c.Count, c.Index });
        chunk.Property(c => c.ConfigHash).HasMaxLength(32);
        chunk.HasOne(c => c.Message)
            .WithOne()
            .HasForeignKey<DataChunk>(c => c.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
        chunk.HasOne(c => c.Attachment)
            .WithMany(a => a.Chunks)
            .HasForeignKey(c => c.AttachmentId)
            .OnDelete(DeleteBehavior.Restrict);

        var collection = modelBuilder.Entity<Collection>();
        collection.HasIndex(c => new { c.Network, c.ProtocolId }).IsUnique();
        collection.Property(c => c.ProtocolId).HasMaxLength(32);
        collection.Property(c => c.ConfigHash).HasMaxLength(32);
        collection.Property(c => c.Name).HasMaxLength(64);
        collection.Property(c => c.PrimaryPayout).HasMaxLength(64);
        collection.Property(c => c.RoyaltyPayout).HasMaxLength(64);
        collection.Property(c => c.PrimaryPrice).HasPrecision(20, 0);
        collection.HasOne(c => c.CreationMessage)
            .WithOne()
            .HasForeignKey<Collection>(c => c.CreationMessageId)
            .OnDelete(DeleteBehavior.Cascade);
        collection.HasOne(c => c.TermsAttachment)
            .WithMany()
            .HasForeignKey(c => c.TermsAttachmentId)
            .OnDelete(DeleteBehavior.Restrict);
        collection.HasOne(c => c.LocationsAttachment)
            .WithMany()
            .HasForeignKey(c => c.LocationsAttachmentId)
            .OnDelete(DeleteBehavior.Restrict);

        var output = modelBuilder.Entity<CollectionOutput>();
        output.HasIndex(o => new { o.SourceMessageId, o.OutputIndex }).IsUnique();
        output.HasIndex(o => new { o.CollectionId, o.Kind });
        output.HasIndex(o => new { o.Network, o.KeyImage }).IsUnique();
        output.Property(o => o.Kind).HasConversion<byte>();
        output.Property(o => o.PublicKey).HasMaxLength(32);
        output.Property(o => o.KeyImage).HasMaxLength(32);
        output.Property(o => o.OwnerKey).HasMaxLength(32);
        output.Property(o => o.NominalAmount).HasPrecision(20, 0);
        output.HasOne(o => o.Collection).WithMany(c => c.Outputs)
            .HasForeignKey(o => o.CollectionId).OnDelete(DeleteBehavior.Cascade);
        output.HasOne(o => o.SourceMessage).WithMany().HasForeignKey(o => o.SourceMessageId).OnDelete(DeleteBehavior.Cascade);

        var split = modelBuilder.Entity<IssuanceSplit>();
        split.HasKey(s => s.MessageId);
        split.HasOne(s => s.Message).WithOne().HasForeignKey<IssuanceSplit>(s => s.MessageId).OnDelete(DeleteBehavior.Cascade);
        split.HasOne(s => s.ParentOutput).WithOne(o => o.Split).HasForeignKey<IssuanceSplit>(s => s.ParentOutputId).OnDelete(DeleteBehavior.Cascade);

        var change = modelBuilder.Entity<CollectionChange>();
        change.HasKey(c => c.MessageId);
        change.HasIndex(c => c.CollectionId);
        change.HasIndex(c => new { c.Network, c.PreviousKeyImage }).IsUnique();
        change.HasIndex(c => new { c.Network, c.KeyImage }).IsUnique();
        change.Property(c => c.PreviousKeyImage).HasMaxLength(32);
        change.Property(c => c.PublicKey).HasMaxLength(32);
        change.Property(c => c.KeyImage).HasMaxLength(32);
        change.Property(c => c.OwnerKey).HasMaxLength(32);
        change.Property(c => c.NominalAmount).HasPrecision(20, 0);
        change.Property(c => c.CollectionMetadataUri).HasMaxLength(1024);
        change.Property(c => c.ItemsMetadataUri).HasMaxLength(1024);
        change.Property(c => c.PlaceholderUri).HasMaxLength(1024);
        change.HasOne(c => c.Message).WithOne().HasForeignKey<CollectionChange>(c => c.MessageId).OnDelete(DeleteBehavior.Cascade);
        change.HasOne(c => c.Collection).WithMany(c => c.Changes).HasForeignKey(c => c.CollectionId).OnDelete(DeleteBehavior.Cascade);
        change.HasOne(c => c.LocationsAttachment).WithMany().HasForeignKey(c => c.LocationsAttachmentId).OnDelete(DeleteBehavior.Restrict);

        var purchase = modelBuilder.Entity<PrimaryPurchase>();
        purchase.HasKey(p => p.MessageId);
        purchase.Property(p => p.CreatorAmount).HasPrecision(20, 0);
        purchase.Property(p => p.PlatformFee).HasPrecision(20, 0);
        purchase.HasOne(p => p.Message).WithOne().HasForeignKey<PrimaryPurchase>(p => p.MessageId).OnDelete(DeleteBehavior.Cascade);
        purchase.HasOne(p => p.Collection).WithMany().HasForeignKey(p => p.CollectionId).OnDelete(DeleteBehavior.Cascade);

        var item = modelBuilder.Entity<PrimaryPurchaseItem>();
        item.HasKey(i => new { i.CollectionId, i.Serial });
        item.HasIndex(i => new { i.CollectionId, i.ItemId }).IsUnique();
        item.Property(i => i.ItemId).HasMaxLength(32);
        item.HasOne(i => i.Purchase).WithMany(p => p.Items).HasForeignKey(i => i.PurchaseMessageId).OnDelete(DeleteBehavior.Cascade);
        item.HasOne<Collection>().WithMany().HasForeignKey(i => i.CollectionId).OnDelete(DeleteBehavior.Cascade);
        item.HasOne(i => i.PreviousOutput).WithOne(o => o.Purchase).HasForeignKey<PrimaryPurchaseItem>(i => i.PreviousOutputId).OnDelete(DeleteBehavior.Cascade);
        item.HasOne(i => i.BuyerOutput).WithOne(o => o.PurchaseOrigin).HasForeignKey<PrimaryPurchaseItem>(i => i.BuyerOutputId).OnDelete(DeleteBehavior.Cascade);
    }
}
