using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Salon.Core.Tenancy;

namespace Salon.Data.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Slug).HasMaxLength(63).IsRequired();
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(t => t.Culture).HasMaxLength(10).IsRequired();
        builder.Property(t => t.Currency).HasMaxLength(3).IsRequired();
    }
}