using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Locations;

namespace Salon.Data.Configurations;

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("locations");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Name).HasMaxLength(Location.NameMaxLength);
        builder.Property(l => l.AddressLine1).HasMaxLength(Location.AddressLineMaxLength).IsRequired();
        builder.Property(l => l.AddressLine2).HasMaxLength(Location.AddressLineMaxLength);
        builder.Property(l => l.City).HasMaxLength(Location.CityMaxLength).IsRequired();
        builder.Property(l => l.Postcode).HasMaxLength(Location.PostcodeMaxLength).IsRequired();
        builder.Property(l => l.Phone).HasMaxLength(Location.PhoneMaxLength);
        builder.Property(l => l.Email).HasMaxLength(Location.EmailMaxLength);
        // The main location is the first active row in this order (ADR 0027).
        builder.HasIndex(l => new { l.TenantId, l.SortOrder }).HasDatabaseName("ix_locations_tenant_id_sort_order");
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}