using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Clients;

namespace Salon.Data.Configurations;

internal sealed class ConsultationRecordConfiguration : IEntityTypeConfiguration<ConsultationRecord>
{
    public void Configure(EntityTypeBuilder<ConsultationRecord> builder)
    {
        builder.ToTable("consultation_records");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.For).HasColumnName("taken_for").HasMaxLength(ConsultationRecord.ForMaxLength);
        builder.Property(r => r.AnswersProtected).IsRequired();
        builder.Property(r => r.ConsentWording).HasMaxLength(ConsultationRecord.ConsentWordingMaxLength).IsRequired();
        // The client's card lists their consultations newest first.
        builder.HasIndex(r => new { r.TenantId, r.ClientId, r.TakenAt });
        // Restrict: a consultation is part of the client's history and outlives a tidy-up of whatever it points at.
        builder.HasOne<Client>().WithMany().HasForeignKey(r => r.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}