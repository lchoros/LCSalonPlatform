using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Salon.Core.Appointments;

namespace Salon.Data.Configurations;

internal sealed class AppointmentStatusChangeConfiguration : IEntityTypeConfiguration<AppointmentStatusChange>
{
    public void Configure(EntityTypeBuilder<AppointmentStatusChange> builder)
    {
        builder.ToTable("appointment_status_changes");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.From).HasConversion<int?>();
        builder.Property(c => c.To).HasConversion<int>();
        builder.Property(c => c.Reason).HasMaxLength(AppointmentStatusChange.ReasonMaxLength);
        builder.HasIndex(c => new { c.AppointmentId, c.At });
        builder.HasOne<Appointment>().WithMany().HasForeignKey(c => c.AppointmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}