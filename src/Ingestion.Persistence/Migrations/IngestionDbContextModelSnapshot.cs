using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Ingestion.Persistence.Migrations;

[DbContext(typeof(IngestionDbContext))]
public sealed class IngestionDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => ConfigureModel(modelBuilder);

    internal static void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

        modelBuilder.Entity<ReceivedValue>(builder =>
        {
            builder.Property(x => x.Id).ValueGeneratedNever().HasColumnType("uuid");
            builder.Property(x => x.IdempotencyKey).HasMaxLength(200).HasColumnName("idempotency_key").IsRequired();
            builder.Property(x => x.RequestFingerprint).HasMaxLength(64).HasColumnName("request_fingerprint").IsRequired();
            builder.Property(x => x.Value).HasColumnName("value").HasColumnType("numeric");
            builder.Property(x => x.ReceivedAt).HasColumnName("received_at").HasColumnType("timestamp with time zone");
            builder.HasKey(x => x.Id);
            builder.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("ux_received_values_idempotency_key");
            builder.ToTable("received_values");
        });

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.Property(x => x.Id).ValueGeneratedNever().HasColumnType("uuid");
            builder.Property(x => x.ValueId).HasColumnName("value_id").HasColumnType("uuid");
            builder.Property(x => x.EventType).HasMaxLength(100).HasColumnName("event_type").IsRequired();
            builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("text").IsRequired();
            builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone");
            builder.Property(x => x.PublishedAt).HasColumnName("published_at").HasColumnType("timestamp with time zone");
            builder.HasKey(x => x.Id);
            builder.HasIndex(x => x.ValueId).IsUnique().HasDatabaseName("ux_outbox_messages_value_id");
            builder.HasOne<ReceivedValue>().WithMany().HasForeignKey(x => x.ValueId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.ToTable("outbox_messages");
        });
    }
}
