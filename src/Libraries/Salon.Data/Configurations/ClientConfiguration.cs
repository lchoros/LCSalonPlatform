using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Salon.Core.Clients;

namespace Salon.Data.Configurations;

internal sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("clients");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.FirstName).HasMaxLength(Client.NameMaxLength).IsRequired();
        builder.Property(c => c.LastName).HasMaxLength(Client.NameMaxLength).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(Client.PhoneMaxLength);
        builder.Property(c => c.Email).HasMaxLength(Client.EmailMaxLength);
        builder.Property(c => c.Notes).HasMaxLength(Client.NotesMaxLength);
        builder.Property(c => c.DepositPercent).HasPrecision(5, 2);
        builder.Property(c => c.HealthNotesProtected).HasMaxLength(Client.HealthNotesProtectedMaxLength);
        builder.Property(c => c.MarketingConsentWording).HasMaxLength(Client.MarketingConsentWordingMaxLength);
        builder.Ignore(c => c.FullName);
        builder.HasIndex(c => new { c.TenantId, c.LastName, c.FirstName });
        builder.HasIndex(c => new { c.TenantId, c.Phone }).IsUnique().HasFilter("phone IS NOT NULL");
        builder.HasIndex(c => new { c.TenantId, c.Email }).IsUnique().HasFilter("email IS NOT NULL");
        builder.HasIndex(c => c.MergedIntoClientId);
        // A merged card points at the one that absorbed it (ADR 0017); the target cannot be deleted while pointers remain.
        builder.HasOne<Client>().WithMany().HasForeignKey(c => c.MergedIntoClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}