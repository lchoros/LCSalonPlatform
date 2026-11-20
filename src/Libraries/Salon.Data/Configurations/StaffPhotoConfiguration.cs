using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Staff;

namespace Salon.Data.Configurations;

internal sealed class StaffPhotoConfiguration : IEntityTypeConfiguration<StaffPhoto>
{
    public void Configure(EntityTypeBuilder<StaffPhoto> builder)
    {
        builder.ToTable("staff_photos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.ContentType).HasMaxLength(StaffPhoto.ContentTypeMaxLength).IsRequired();
        builder.Property(p => p.Bytes).IsRequired();
        // One face per person (ADR 0088): a second upload replaces the picture, it never adds one.
        builder.HasIndex(p => p.StaffMemberId).IsUnique().HasDatabaseName("ux_staff_photos_staff_member_id");
        // The picture is the person's own data: it goes with them.
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(p => p.StaffMemberId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}