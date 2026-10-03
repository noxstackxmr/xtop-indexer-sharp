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
        chunk.HasOne(c => c.Message)
            .WithOne()
            .HasForeignKey<DataChunk>(c => c.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
        chunk.HasOne(c => c.Attachment)
            .WithMany(a => a.Chunks)
            .HasForeignKey(c => c.AttachmentId)
            .OnDelete(DeleteBehavior.Restrict);

        var collection = modelBuilder.Entity<Collection>();
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
    }
}
