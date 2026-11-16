using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Core.Clients;
using Salon.Core.Staff;

namespace Salon.Data.Configurations;

internal sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        builder.ToTable("waitlist_entries", t =>
        {
            t.HasCheckConstraint("ck_waitlist_entries_days_in_order", "not_before IS NULL OR not_after IS NULL OR not_after >= not_before");
            // Closed means both the moment and how it ended (ADR 0041); waiting means neither.
            t.HasCheckConstraint("ck_waitlist_entries_closed_with_outcome", "(closed_at IS NULL) = (outcome IS NULL)");
        });
        builder.HasKey(w => w.Id);
        builder.Property(w => w.NotBefore).HasColumnType("date");
        builder.Property(w => w.NotAfter).HasColumnType("date");
        builder.Property(w => w.Note).HasMaxLength(WaitlistEntry.NoteMaxLength);
        builder.Property(w => w.Outcome).HasConversion<int?>();
        // The list reads the waiting entries oldest first.
        builder.HasIndex(w => new { w.TenantId, w.CreatedAt }).HasFilter("closed_at IS NULL").HasDatabaseName("ix_waitlist_entries_waiting");
        // A client waits once for a service (ADR 0041): asking again is the same wish.
        builder.HasIndex(w => new { w.TenantId, w.ClientId, w.ServiceId })
            .IsUnique()
            .HasFilter("closed_at IS NULL")
            .HasDatabaseName("ux_waitlist_entries_one_wait_per_client_and_service");
        builder.HasOne<Client>().WithMany().HasForeignKey(w => w.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Service>().WithMany().HasForeignKey(w => w.ServiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(w => w.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Appointment>().WithMany().HasForeignKey(w => w.AppointmentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(w => w.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}