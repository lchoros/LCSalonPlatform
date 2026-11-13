using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Events;

namespace Salon.Data.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.EventType).HasMaxLength(300).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(OutboxMessage.MaxErrorLength);
        // Which consumers are done with the event (ADR 0025); existing rows start with none.
        builder.Property(m => m.CompletedConsumers).HasColumnType("text[]").HasDefaultValueSql("'{}'");
        // Postgres' xmin as the row version: two dispatchers updating the same row make the slower one fail with a concurrency exception.
        builder.Property<uint>("xmin").IsRowVersion();
        builder.HasIndex(m => m.TenantId);
        // Due rows only; the dispatcher's query filters and orders on exactly these columns.
        builder.HasIndex(m => new { m.NextAttemptAt, m.Id })
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter("processed_at IS NULL AND failed_at IS NULL");
    }
}