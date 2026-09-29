using Microsoft.EntityFrameworkCore;

namespace IndexerCore.Data;

public sealed class IndexerDbContext(DbContextOptions<IndexerDbContext> options) : DbContext(options)
{
}
