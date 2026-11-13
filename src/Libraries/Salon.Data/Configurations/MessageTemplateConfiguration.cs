using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Messaging;

namespace Salon.Data.Configurations;

internal sealed class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> builder)
    {
        builder.ToTable("message_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Key).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Channel).HasConversion<int>();
        builder.Property(t => t.Culture).HasMaxLength(10).IsRequired();
        builder.Property(t => t.Subject).HasMaxLength(200);
        builder.Property(t => t.Body).HasColumnType("text").IsRequired();
        builder.HasIndex(t => new { t.TenantId, t.Key, t.Channel, t.Culture }).IsUnique();
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}