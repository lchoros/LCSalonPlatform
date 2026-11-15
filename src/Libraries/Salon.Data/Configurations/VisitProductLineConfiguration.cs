using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Appointments;
using Salon.Core.Payments;
using Salon.Core.Products;

namespace Salon.Data.Configurations;

internal sealed class VisitProductLineConfiguration : IEntityTypeConfiguration<VisitProductLine>
{
    public void Configure(EntityTypeBuilder<VisitProductLine> builder)
    {
        builder.ToTable("visit_product_lines", t =>
        {
            t.HasCheckConstraint("ck_visit_product_lines_quantity_positive", "quantity > 0");
            t.HasCheckConstraint("ck_visit_product_lines_unit_price_not_negative", "unit_price >= 0");
        });
        builder.HasKey(l => l.Id);
        builder.Property(l => l.ProductName).HasMaxLength(ProductRules.NameMaxLength).IsRequired();
        builder.Property(l => l.UnitPrice).HasPrecision(10, 2);
        builder.Property(l => l.Currency).HasMaxLength(VisitProductLine.CurrencyLength).IsRequired();
        builder.Ignore(l => l.IsPaid);
        builder.Ignore(l => l.Total);
        // The checkout reads a visit's lines in the order they were rung up.
        builder.HasIndex(l => new { l.TenantId, l.AppointmentId, l.AddedAt });
        // One unpaid line per product on a bill (ADR 0037): ringing the same product up again raises its quantity, and
        // two tills doing it at once cannot leave two lines.
        builder.HasIndex(l => new { l.TenantId, l.AppointmentId, l.ProductId })
            .IsUnique()
            .HasFilter("payment_entry_id IS NULL")
            .HasDatabaseName("ux_visit_product_lines_one_unpaid_line_per_product");
        // Restrict everywhere: a paid line is money history and outlives a tidy-up of whatever it points at.
        builder.HasOne<Appointment>().WithMany().HasForeignKey(l => l.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClientAccountEntry>().WithMany().HasForeignKey(l => l.PaymentEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}