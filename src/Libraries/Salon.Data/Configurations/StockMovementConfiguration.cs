using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Appointments;
using Salon.Core.Products;

namespace Salon.Data.Configurations;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", t =>
        {
            t.HasCheckConstraint("ck_stock_movements_quantity_not_zero", "quantity <> 0");
            t.HasCheckConstraint("ck_stock_movements_on_hand_after_not_negative", "on_hand_after >= 0");
        });
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Kind).HasConversion<int>();
        builder.Property(m => m.Note).HasMaxLength(StockMovement.NoteMaxLength);
        // The product page lists the history newest first.
        builder.HasIndex(m => new { m.TenantId, m.ProductId, m.At });
        // Restrict: the history outlives a tidy-up of whatever it points at.
        builder.HasOne<Product>().WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
        // The visit a sale belongs to (ADR 0037).
        builder.HasOne<Appointment>().WithMany().HasForeignKey(m => m.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}