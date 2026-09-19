using Microsoft.EntityFrameworkCore;

namespace Ingestion.Persistence.Migrations;

public partial class OutboxTraceContext
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
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
            builder.Property(x => x.TraceParent).HasColumnName("traceparent").HasMaxLength(256);
            builder.Property(x => x.TraceState).HasColumnName("tracestate").HasMaxLength(512);
            builder.Property(x => x.PublishedAt).HasColumnName("published_at").HasColumnType("timestamp with time zone");
            builder.Property(x => x.QuarantinedAt).HasColumnName("quarantined_at").HasColumnType("timestamp with time zone");
            builder.Property(x => x.QuarantineReason).HasColumnName("quarantine_reason").HasMaxLength(200);
            builder.Property(x => x.PublishAttempts).HasColumnName("publish_attempts").HasDefaultValue(0);
            builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at").HasColumnType("timestamp with time zone");
            builder.HasKey(x => x.Id);
            builder.HasIndex(x => x.ValueId).IsUnique().HasDatabaseName("ux_outbox_messages_value_id");
            builder.HasOne<ReceivedValue>().WithMany().HasForeignKey(x => x.ValueId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.ToTable("outbox_messages");
        });
    }
}
