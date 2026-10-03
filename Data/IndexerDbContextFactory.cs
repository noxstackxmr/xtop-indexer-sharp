using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IndexerCore.Data;

public sealed class IndexerDbContextFactory : IDesignTimeDbContextFactory<IndexerDbContext>
{
    public IndexerDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<IndexerDbContext>()
            .UseNpgsql("Host=localhost;Database=xtop_indexer;Username=postgres").Options);
}
