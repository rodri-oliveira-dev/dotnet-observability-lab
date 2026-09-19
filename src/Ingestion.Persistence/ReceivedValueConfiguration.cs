using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ingestion.Persistence;

public sealed class ReceivedValueConfiguration : IEntityTypeConfiguration<ReceivedValue>
{
    public void Configure(EntityTypeBuilder<ReceivedValue> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("received_values");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.RequestFingerprint).HasColumnName("request_fingerprint").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Value).HasColumnName("value").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.ReceivedAt).HasColumnName("received_at").IsRequired();
        builder.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("ux_received_values_idempotency_key");
    }
}
