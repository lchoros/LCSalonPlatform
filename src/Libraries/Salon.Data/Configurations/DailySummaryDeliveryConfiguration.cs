using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Notifications;

namespace Salon.Data.Configurations;

internal sealed class DailySummaryDeliveryConfiguration : IEntityTypeConfiguration<DailySummaryDelivery>
{
    public void Configure(EntityTypeBuilder<DailySummaryDelivery> builder)
    {
        builder.ToTable("daily_summary_deliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Day).HasColumnType("date");
        // The guarantee that a day is never sent twice (ADR 0050): the row is the claim.
        builder.HasIndex(d => new { d.TenantId, d.Day }).IsUnique();
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}