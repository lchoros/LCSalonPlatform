using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Core.Clients;
using Salon.Core.Staff;

namespace Salon.Data.Configurations;

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Start).HasColumnName("start_at");
        builder.Property(a => a.End).HasColumnName("end_at");
        builder.Property(a => a.Status).HasConversion<int>();
        builder.Property(a => a.Source).HasConversion<int>();
        builder.Property(a => a.Notes).HasMaxLength(Appointment.NotesMaxLength);
        builder.Property(a => a.PriceAmount).HasPrecision(10, 2);
        builder.Property(a => a.DepositAmount).HasPrecision(10, 2).HasDefaultValue(0m);
        builder.Property(a => a.Currency).HasMaxLength(Appointment.CurrencyLength).IsRequired();
        builder.Property(a => a.CancellationReason).HasMaxLength(Appointment.CancellationReasonMaxLength);
        builder.Property(a => a.PublicToken).HasMaxLength(Appointment.PublicTokenMaxLength);
        // The cancel link resolves by token alone (ADR 0014); the tenant filter still applies on top.
        builder.HasIndex(a => a.PublicToken).IsUnique().HasFilter("public_token IS NOT NULL");
        builder.Ignore(a => a.Price);
        builder.Ignore(a => a.Interval);
        // The client card lists its visits; ADR 0012 added the FK once the clients slice (ADR 0011) was merged.
        builder.HasIndex(a => new { a.TenantId, a.ClientId });
        // The day view and the availability engine both ask "this person's appointments in this window".
        builder.HasIndex(a => new { a.TenantId, a.StaffMemberId, a.Start });
        // The exclusion constraint (no two active appointments of one person overlap) is raw SQL in the Appointments migration.
        builder.HasOne<Client>().WithMany().HasForeignKey(a => a.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(a => a.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Service>().WithMany().HasForeignKey(a => a.ServiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}