using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Salon.Core.Booking;
using Salon.Core.Catalog;

namespace Salon.Data.Configurations;

internal sealed class PhoneVerificationConfiguration : IEntityTypeConfiguration<PhoneVerification>
{
    public void Configure(EntityTypeBuilder<PhoneVerification> builder)
    {
        builder.ToTable("phone_verifications");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.PhoneE164).HasMaxLength(PhoneVerification.PhoneMaxLength).IsRequired();
        builder.Property(v => v.CodeSalt).HasMaxLength(PhoneVerification.HashMaxLength).IsRequired();
        builder.Property(v => v.CodeHash).HasMaxLength(PhoneVerification.HashMaxLength).IsRequired();
        builder.Property(v => v.Start).HasColumnName("start_at");
        builder.Property(v => v.FirstName).HasMaxLength(PhoneVerification.NameMaxLength).IsRequired();
        builder.Property(v => v.LastName).HasMaxLength(PhoneVerification.NameMaxLength).IsRequired();
        builder.Property(v => v.Email).HasMaxLength(PhoneVerification.EmailMaxLength);
        builder.Property(v => v.ConsentWording).HasMaxLength(PhoneVerification.ConsentWordingMaxLength);
        // "How many codes went to this number in the last hour" (ADR 0014 anti-abuse).
        builder.HasIndex(v => new { v.TenantId, v.PhoneE164, v.CreatedAt });
        builder.HasOne<Service>().WithMany().HasForeignKey(v => v.ServiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(v => v.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
