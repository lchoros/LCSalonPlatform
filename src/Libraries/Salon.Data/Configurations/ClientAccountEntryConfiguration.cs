using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Salon.Core.Appointments;
using Salon.Core.Clients;
using Salon.Core.Payments;

namespace Salon.Data.Configurations;

internal sealed class ClientAccountEntryConfiguration : IEntityTypeConfiguration<ClientAccountEntry>
{
    public void Configure(EntityTypeBuilder<ClientAccountEntry> builder)
    {
        builder.ToTable("client_account_entries", t => t.HasCheckConstraint("ck_client_account_entries_amount_positive", "amount > 0"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Kind).HasConversion<int>();
        builder.Property(e => e.Method).HasConversion<int?>();
        builder.Property(e => e.Amount).HasPrecision(10, 2);
        builder.Property(e => e.Currency).HasMaxLength(ClientAccountEntry.CurrencyLength).IsRequired();
        builder.Property(e => e.ProviderSystemName).HasMaxLength(ClientAccountEntry.ProviderNameMaxLength);
        builder.Property(e => e.ProviderReference).HasMaxLength(ClientAccountEntry.ProviderReferenceMaxLength);
        builder.Property(e => e.Note).HasMaxLength(ClientAccountEntry.NoteMaxLength);
        builder.Ignore(e => e.Money);
        // The client card lists the account newest first.
        builder.HasIndex(e => new { e.TenantId, e.ClientId, e.At });
        // One fee per visit and one settlement per fee (ADR 0022): a double click or two tills cannot charge or settle twice.
        builder.HasIndex(e => new { e.TenantId, e.AppointmentId })
            .IsUnique()
            .HasFilter("kind IN (1, 2)")
            .HasDatabaseName("ux_client_account_entries_one_fee_per_appointment");
        builder.HasIndex(e => new { e.TenantId, e.RelatedEntryId })
            .IsUnique()
            .HasFilter("kind IN (3, 4)")
            .HasDatabaseName("ux_client_account_entries_one_settlement_per_fee");
        // Likewise one deposit per visit and one return per deposit (ADR 0023).
        builder.HasIndex(e => new { e.TenantId, e.AppointmentId }, "OneDepositPerAppointment")
            .IsUnique()
            .HasFilter("kind = 5")
            .HasDatabaseName("ux_client_account_entries_one_deposit_per_appointment");
        builder.HasIndex(e => new { e.TenantId, e.RelatedEntryId }, "OneReturnPerDeposit")
            .IsUnique()
            .HasFilter("kind = 6")
            .HasDatabaseName("ux_client_account_entries_one_return_per_deposit");
        // Restrict everywhere: money history outlives a tidy-up of whatever it points at.
        builder.HasOne<Client>().WithMany().HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Appointment>().WithMany().HasForeignKey(e => e.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClientAccountEntry>().WithMany().HasForeignKey(e => e.RelatedEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}