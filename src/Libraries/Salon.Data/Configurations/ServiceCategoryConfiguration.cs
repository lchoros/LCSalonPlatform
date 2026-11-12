using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Salon.Core.Catalog;

namespace Salon.Data.Configurations;

internal sealed class ServiceCategoryConfiguration : IEntityTypeConfiguration<ServiceCategory>
{
    public void Configure(EntityTypeBuilder<ServiceCategory> builder)
    {
        builder.ToTable("service_categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(ServiceCategory.NameMaxLength).IsRequired();
        // The validators refuse a duplicate case-insensitively before this fires; the index is the backstop.
        builder.HasIndex(c => new { c.TenantId, c.Name }).IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.SortOrder });
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}