using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Salon.Core.Staff;

namespace Salon.Data.Configurations;

internal sealed class StaffTimeOffConfiguration : IEntityTypeConfiguration<StaffTimeOff>
{
    public void Configure(EntityTypeBuilder<StaffTimeOff> builder)
    {
        builder.ToTable("staff_time_off");
        builder.HasKey(t => t.Id);
        // LocalDate/LocalTime map to Postgres `date`/`time` through the Npgsql NodaTime plugin; the salon's zone is applied when slots are computed (ADR 0015).
        builder.Property(t => t.FromDate).HasColumnType("date");
        builder.Property(t => t.ToDate).HasColumnType("date");
        builder.Property(t => t.Start).HasColumnType("time");
        builder.Property(t => t.End).HasColumnType("time");
        builder.Property(t => t.Reason).HasMaxLength(StaffTimeOff.ReasonMaxLength);
        builder.Ignore(t => t.IsWholeDay);
        builder.Ignore(t => t.Range);
        // The engine asks "this person's entries touching a window"; the page asks "this person's entries, newest first".
        builder.HasIndex(t => new { t.TenantId, t.StaffMemberId, t.FromDate });
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(t => t.StaffMemberId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}