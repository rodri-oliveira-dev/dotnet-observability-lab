using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Consolidation.Persistence;

public sealed class ConsolidationDbContextFactory : IDesignTimeDbContextFactory<ConsolidationDbContext>
{
    private const string DefaultDesignTimeConnectionString =
        "Host=127.0.0.1;Database=consolidation_db;Username=postgres";

    /// <inheritdoc />
    public ConsolidationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CONSOLIDATION_DB_CONNECTION_STRING")
            ?? DefaultDesignTimeConnectionString;

        var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ConsolidationDbContext(options);
    }
}
