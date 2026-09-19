using Microsoft.EntityFrameworkCore;

namespace Ingestion.Persistence.Migrations;

public partial class InitialIngestion
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        IngestionDbContextModelSnapshot.ConfigureModel(modelBuilder);
}
