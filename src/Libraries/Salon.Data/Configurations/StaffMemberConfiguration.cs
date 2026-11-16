using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Staff;

namespace Salon.Data.Configurations;

internal sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.ToTable("staff_members");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.DisplayName).HasMaxLength(StaffMember.DisplayNameMaxLength).IsRequired();
        builder.Property(s => s.Email).HasMaxLength(StaffMember.EmailMaxLength);
        builder.Property(s => s.Phone).HasMaxLength(StaffMember.PhoneMaxLength);
        builder.Property(s => s.Colour).HasMaxLength(StaffMember.ColourLength).IsRequired();
        // UserId is deliberately a plain column (ADR 0010): a person may exist without a login and a login may go without touching the rota.
        builder.HasIndex(s => new { s.TenantId, s.SortOrder });
        // A calendar address is looked up by its secret alone (ADR 0038); two people never share one.
        builder.Property(s => s.CalendarFeedToken).HasMaxLength(64);
        builder.HasIndex(s => s.CalendarFeedToken).IsUnique().HasFilter("calendar_feed_token IS NOT NULL");
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}