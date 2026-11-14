using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Payments;

namespace Salon.Data.Configurations;

internal sealed class CashUpConfiguration : IEntityTypeConfiguration<CashUp>
{
    public void Configure(EntityTypeBuilder<CashUp> builder)
    {
        builder.ToTable("cash_ups", t => t.HasCheckConstraint("ck_cash_ups_counted_cash_not_negative", "counted_cash >= 0"));
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ExpectedCash).HasPrecision(12, 2);
        builder.Property(c => c.CountedCash).HasPrecision(12, 2);
        builder.Property(c => c.Currency).HasMaxLength(CashUp.CurrencyLength).IsRequired();
        builder.Property(c => c.Note).HasMaxLength(CashUp.NoteMaxLength);
        builder.Ignore(c => c.Difference);
        // A day is closed once (ADR 0024): the row is the claim, a second close is refused by the database.
        builder.HasIndex(c => new { c.TenantId, c.Day }).IsUnique();
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}