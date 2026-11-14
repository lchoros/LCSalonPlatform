using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Staff;

namespace Salon.Data.Configurations;

internal sealed class WorkingHoursConfiguration : IEntityTypeConfiguration<WorkingHours>
{
    public void Configure(EntityTypeBuilder<WorkingHours> builder)
    {
        builder.ToTable("working_hours");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Day).HasConversion<int>();
        // LocalTime maps to Postgres `time` through the Npgsql NodaTime plugin; the salon's zone is applied when slots are computed.
        builder.Property(w => w.Start).HasColumnType("time");
        builder.Property(w => w.End).HasColumnType("time");
        builder.Ignore(w => w.Range);
        // Two ranges of one person on one day cannot start at the same minute; the command validator rejects overlaps before this fires.
        builder.HasIndex(w => new { w.TenantId, w.StaffMemberId, w.Day, w.Start }).IsUnique();
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(w => w.StaffMemberId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(w => w.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}