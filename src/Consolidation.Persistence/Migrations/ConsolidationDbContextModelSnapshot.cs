using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
namespace Consolidation.Persistence.Migrations;
[DbContext(typeof(ConsolidationDbContext))]
public sealed class ConsolidationDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.12");
        modelBuilder.Entity<InboxMessage>(e =>
        {
            e.ToTable("inbox_messages");
            e.HasKey(x => x.MessageId);
            e.Property(x => x.MessageId).HasColumnName("message_id").ValueGeneratedNever();
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        });
        modelBuilder.Entity<ConsolidatedTotal>(e =>
        {
            e.ToTable("consolidated_totals", t => t.HasCheckConstraint("ck_consolidated_totals_singleton", "id = 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.Count).HasColumnName("count");
            e.Property(x => x.Sum).HasColumnName("sum").HasColumnType("numeric");
            e.Property(x => x.LastUpdatedAt).HasColumnName("last_updated_at");
            e.Ignore(x => x.Average);
        });
    }
}
