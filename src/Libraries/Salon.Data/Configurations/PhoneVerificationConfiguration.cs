using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Booking;
using Salon.Core.Catalog;

namespace Salon.Data.Configurations;

internal sealed class PhoneVerificationConfiguration : IEntityTypeConfiguration<PhoneVerification>
{
    public void Configure(EntityTypeBuilder<PhoneVerification> builder)
    {
        // A booking or a waitlist request always names its service; only a row that proves a number for the client's
        // own pages (purpose 3, ADR 0083) carries none.
        builder.ToTable("phone_verifications", t => t.HasCheckConstraint("ck_phone_verifications_service_unless_client_portal", "service_id IS NOT NULL OR purpose = 3"));
        builder.HasKey(v => v.Id);
        builder.Property(v => v.PhoneE164).HasMaxLength(PhoneVerification.PhoneMaxLength).IsRequired();
        builder.Property(v => v.CodeSalt).HasMaxLength(PhoneVerification.HashMaxLength).IsRequired();
        builder.Property(v => v.CodeHash).HasMaxLength(PhoneVerification.HashMaxLength).IsRequired();
        builder.Property(v => v.Start).HasColumnName("start_at");
        builder.Property(v => v.ExtraServiceIds).HasColumnType("uuid[]").HasDefaultValueSql("'{}'::uuid[]").IsRequired();
        builder.Property(v => v.ExtraStaffIds).HasColumnType("uuid[]").HasDefaultValueSql("'{}'::uuid[]").IsRequired();
        builder.Property(v => v.FirstName).HasMaxLength(PhoneVerification.NameMaxLength).IsRequired();
        builder.Property(v => v.LastName).HasMaxLength(PhoneVerification.NameMaxLength).IsRequired();
        builder.Property(v => v.Email).HasMaxLength(PhoneVerification.EmailMaxLength);
        builder.Property(v => v.ConsentWording).HasMaxLength(PhoneVerification.ConsentWordingMaxLength);
        builder.Property(v => v.Purpose).HasConversion<int>().HasDefaultValue(PhoneVerificationPurpose.Booking);
        builder.Property(v => v.WaitNotBefore).HasColumnType("date");
        builder.Property(v => v.WaitNotAfter).HasColumnType("date");
        builder.Property(v => v.WaitNote).HasMaxLength(Core.Appointments.WaitlistEntry.NoteMaxLength);
        // "How many codes went to this number in the last hour" (ADR 0014 anti-abuse).
        builder.HasIndex(v => new { v.TenantId, v.PhoneE164, v.CreatedAt });
        builder.HasOne<Service>().WithMany().HasForeignKey(v => v.ServiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(v => v.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}