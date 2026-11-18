using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Payments;

namespace Salon.Data.Configurations;

internal sealed class GiftVoucherConfiguration : IEntityTypeConfiguration<GiftVoucher>
{
    public void Configure(EntityTypeBuilder<GiftVoucher> builder)
    {
        builder.ToTable("gift_vouchers", t =>
        {
            t.HasCheckConstraint("ck_gift_vouchers_initial_amount_positive", "initial_amount > 0");
            // What is left never goes below zero and never above what the voucher was sold for (ADR 0057).
            t.HasCheckConstraint("ck_gift_vouchers_balance_within_initial", "balance >= 0 AND balance <= initial_amount");
        });
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Code).HasMaxLength(GiftVoucher.CodeLength).IsRequired();
        builder.Property(v => v.InitialAmount).HasPrecision(10, 2);
        builder.Property(v => v.Balance).HasPrecision(10, 2);
        builder.Property(v => v.Currency).HasMaxLength(GiftVoucher.CurrencyLength).IsRequired();
        builder.Property(v => v.ExpiresOn).HasColumnType("date");
        builder.Property(v => v.Note).HasMaxLength(GiftVoucher.NoteMaxLength);
        builder.Ignore(v => v.Initial);
        builder.Ignore(v => v.Left);
        // The desk finds a voucher by its code; two vouchers of one salon never share one.
        builder.HasIndex(v => new { v.TenantId, v.Code }).IsUnique();
        // One voucher per sale: the line of money is the sale.
        builder.HasIndex(v => new { v.TenantId, v.SaleEntryId }).IsUnique();
        // Restrict everywhere: a voucher is money the salon owes and outlives a tidy-up of whatever it points at.
        builder.HasOne<ClientAccountEntry>().WithMany().HasForeignKey(v => v.SaleEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(v => v.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}