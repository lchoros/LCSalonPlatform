using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Payments;

namespace Salon.Data.Configurations;

internal sealed class SalonPaymentMethodConfiguration : IEntityTypeConfiguration<SalonPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SalonPaymentMethod> builder)
    {
        builder.ToTable("salon_payment_methods");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).HasMaxLength(SalonPaymentMethod.NameMaxLength).IsRequired();
        // The lists show the methods in the salon's order.
        builder.HasIndex(m => new { m.TenantId, m.SortOrder });
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}