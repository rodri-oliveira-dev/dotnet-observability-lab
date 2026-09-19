using Microsoft.EntityFrameworkCore;

namespace Consolidation.Persistence;

public sealed class ConsolidationDbContext(DbContextOptions<ConsolidationDbContext> options)
    : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConsolidationDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
