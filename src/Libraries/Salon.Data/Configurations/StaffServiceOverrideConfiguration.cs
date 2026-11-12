using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Salon.Core.Catalog;
using Salon.Core.Staff;

namespace Salon.Data.Configurations;

internal sealed class StaffServiceOverrideConfiguration : IEntityTypeConfiguration<StaffServiceOverride>
{
    public void Configure(EntityTypeBuilder<StaffServiceOverride> builder)
    {
        builder.ToTable("staff_service_overrides");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Price).HasPrecision(12, 2);
        builder.Ignore(o => o.IsDefault);
        // One row per person and service; the handler upserts, so this is the backstop.
        builder.HasIndex(o => new { o.TenantId, o.StaffMemberId, o.ServiceId }).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.ServiceId });
        // A deleted person or service takes its overrides with it; the appointments keep their own snapshot (ADR 0012).
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(o => o.StaffMemberId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Service>().WithMany().HasForeignKey(o => o.ServiceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(o => o.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
