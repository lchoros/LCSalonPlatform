using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Salon.Core.Catalog;

namespace Salon.Data.Configurations;

internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(ServiceRules.NameMaxLength).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(ServiceRules.DescriptionMaxLength);
        builder.Property(s => s.Price).HasPrecision(12, 2);
        builder.Property(s => s.Currency).HasMaxLength(3).IsRequired();
        builder.HasIndex(s => new { s.TenantId, s.Name }).IsUnique();
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        // A deleted category leaves its services in place, uncategorised (ADR 0013).
        builder.HasOne<ServiceCategory>().WithMany().HasForeignKey(s => s.CategoryId).OnDelete(DeleteBehavior.SetNull);
    }
}