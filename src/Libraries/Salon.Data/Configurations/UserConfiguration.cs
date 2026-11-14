using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Identity;

namespace Salon.Data.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).HasMaxLength(User.EmailMaxLength).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(User.DisplayNameMaxLength).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(u => u.Role).HasConversion<int>();
        builder.Property(u => u.SecurityStamp).HasMaxLength(64).IsRequired();
        builder.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}