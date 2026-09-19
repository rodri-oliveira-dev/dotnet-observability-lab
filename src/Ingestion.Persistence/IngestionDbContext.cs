using Microsoft.EntityFrameworkCore;

namespace Ingestion.Persistence;

public sealed class IngestionDbContext(DbContextOptions<IngestionDbContext> options)
    : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IngestionDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
