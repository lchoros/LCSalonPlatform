using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Core.Appointments;
using Salon.Core.Booking;
using Salon.Core.Catalog;
using Salon.Core.Staff;

namespace Salon.Data.Configurations;

internal sealed class WaitlistOfferConfiguration : IEntityTypeConfiguration<WaitlistOffer>
{
    public void Configure(EntityTypeBuilder<WaitlistOffer> builder)
    {
        builder.ToTable("waitlist_offers", t =>
        {
            t.HasCheckConstraint("ck_waitlist_offers_ends_after_start", "\"end\" > start");
            // Ended means both the moment and how (ADR 0065); standing means neither.
            t.HasCheckConstraint("ck_waitlist_offers_ended_with_outcome", "(ended_at IS NULL) = (outcome IS NULL)");
        });
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Token).HasMaxLength(PublicBookingToken.Length).IsRequired();
        builder.Property(o => o.Outcome).HasConversion<int?>();
        // The link in the text finds its offer, in whichever salon (the salon's address narrows it first).
        builder.HasIndex(o => o.Token).IsUnique().HasDatabaseName("ux_waitlist_offers_token");
        // One time is held for one client, and one client holds one time (ADR 0065): two cancellations at the same
        // moment, or the same event delivered twice, cannot hold a time twice or give one person two.
        builder.HasIndex(o => new { o.TenantId, o.StaffMemberId, o.Start })
            .IsUnique()
            .HasFilter("ended_at IS NULL")
            .HasDatabaseName("ux_waitlist_offers_one_standing_per_time");
        builder.HasIndex(o => new { o.TenantId, o.WaitlistEntryId })
            .IsUnique()
            .HasFilter("ended_at IS NULL")
            .HasDatabaseName("ux_waitlist_offers_one_standing_per_entry");
        // An offer is part of the wish it answers: it goes when the wish is deleted (a client forgotten, ADR 0062).
        builder.HasOne<WaitlistEntry>().WithMany().HasForeignKey(o => o.WaitlistEntryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Service>().WithMany().HasForeignKey(o => o.ServiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(o => o.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Appointment>().WithMany().HasForeignKey(o => o.AppointmentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Appointment>().WithMany().HasForeignKey(o => o.FreedByAppointmentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Core.Tenancy.Tenant>().WithMany().HasForeignKey(o => o.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}