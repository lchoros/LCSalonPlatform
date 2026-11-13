using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Appointments;
using Salon.Core.Notifications;

namespace Salon.Data.Configurations;

internal sealed class AppointmentReminderConfiguration : IEntityTypeConfiguration<AppointmentReminder>
{
    public void Configure(EntityTypeBuilder<AppointmentReminder> builder)
    {
        builder.ToTable("appointment_reminders");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Kind).HasConversion<int>();
        builder.Property(r => r.Outcome).HasConversion<int>();
        // The guarantee that a client is never texted twice for one moment (ADR 0020): the row is the claim.
        builder.HasIndex(r => new { r.TenantId, r.AppointmentId, r.Kind, r.AppointmentStart }).IsUnique();
        builder.HasOne<Appointment>().WithMany().HasForeignKey(r => r.AppointmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}