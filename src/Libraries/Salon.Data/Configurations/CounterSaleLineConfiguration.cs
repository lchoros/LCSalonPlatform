using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Payments;
using Salon.Core.Products;

namespace Salon.Data.Configurations;

internal sealed class CounterSaleLineConfiguration : IEntityTypeConfiguration<CounterSaleLine>
{
    public void Configure(EntityTypeBuilder<CounterSaleLine> builder)
    {
        builder.ToTable("counter_sale_lines", t =>
        {
            t.HasCheckConstraint("ck_counter_sale_lines_quantity_positive", "quantity > 0");
            t.HasCheckConstraint("ck_counter_sale_lines_unit_price_not_negative", "unit_price >= 0");
            // Nobody brings back more than they bought (ADR 0054).
            t.HasCheckConstraint("ck_counter_sale_lines_returned_within_quantity", "returned_quantity >= 0 AND returned_quantity <= quantity");
        });
        builder.HasKey(l => l.Id);
        builder.Property(l => l.ProductName).HasMaxLength(ProductRules.NameMaxLength).IsRequired();
        builder.Property(l => l.UnitPrice).HasPrecision(10, 2);
        builder.Property(l => l.UnitCost).HasPrecision(10, 2);
        builder.Property(l => l.Currency).HasMaxLength(CounterSaleLine.CurrencyLength).IsRequired();
        builder.Ignore(l => l.Total);
        builder.Ignore(l => l.Kept);
        // A product is on a sale once (ADR 0053): the same product chosen twice is one line with more units.
        builder.HasIndex(l => new { l.TenantId, l.PaymentEntryId, l.ProductId }).IsUnique();
        // Restrict everywhere: a sale is money history and outlives a tidy-up of whatever it points at.
        builder.HasOne<ClientAccountEntry>().WithMany().HasForeignKey(l => l.PaymentEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}