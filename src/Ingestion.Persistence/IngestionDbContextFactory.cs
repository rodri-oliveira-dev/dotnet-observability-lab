using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ingestion.Persistence;

public sealed class IngestionDbContextFactory : IDesignTimeDbContextFactory<IngestionDbContext>
{
    private const string DefaultDesignTimeConnectionString =
        "Host=127.0.0.1;Database=ingestion_db;Username=postgres";

    /// <inheritdoc />
    public IngestionDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("INGESTION_DB_CONNECTION_STRING")
            ?? DefaultDesignTimeConnectionString;

        var options = new DbContextOptionsBuilder<IngestionDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new IngestionDbContext(options);
    }
}
