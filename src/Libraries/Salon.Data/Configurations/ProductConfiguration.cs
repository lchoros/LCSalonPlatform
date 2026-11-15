using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Products;

namespace Salon.Data.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", t =>
        {
            // The shelf never goes below zero (ADR 0036): the service refuses first, this is the backstop.
            t.HasCheckConstraint("ck_products_stock_on_hand_not_negative", "stock_on_hand >= 0");
            t.HasCheckConstraint("ck_products_retail_price_not_negative", "retail_price IS NULL OR retail_price >= 0");
        });
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(ProductRules.NameMaxLength).IsRequired();
        builder.Property(p => p.Brand).HasMaxLength(ProductRules.BrandMaxLength);
        builder.Property(p => p.Code).HasMaxLength(ProductRules.CodeMaxLength);
        builder.Property(p => p.RetailPrice).HasPrecision(10, 2);
        builder.Property(p => p.Currency).HasMaxLength(Product.CurrencyLength).IsRequired();
        builder.Ignore(p => p.Price);
        // Postgres' xmin as the row version: two people changing the same shelf at once make the slower one fail
        // instead of losing the faster one's line.
        builder.Property<uint>("xmin").IsRowVersion();
        // The validators refuse a duplicate case-insensitively before this fires; the index is the backstop.
        builder.HasIndex(p => new { p.TenantId, p.Name }).IsUnique();
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}