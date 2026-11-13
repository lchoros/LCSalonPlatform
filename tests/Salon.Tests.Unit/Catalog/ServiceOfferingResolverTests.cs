using NodaTime;
using Salon.Core.Catalog;
using Salon.Services.Catalog;

namespace Salon.Tests.Unit.Catalog;

/// <summary>
/// The pure half of the offering resolver (ADR 0018): what one staff member effectively offers for a service, and the
/// "from" price span across the staff who offer it. The database half is covered by the integration tests.
/// </summary>
public class ServiceOfferingResolverTests
{
    private static readonly Guid Anna = Guid.Parse("0190a1b2-c3d4-7e5f-8a9b-000000000001");
    private static readonly Guid Tom = Guid.Parse("0190a1b2-c3d4-7e5f-8a9b-000000000002");
    private static readonly Guid Priya = Guid.Parse("0190a1b2-c3d4-7e5f-8a9b-000000000003");

    private static readonly Service Cut = new()
    {
        Name = "Cut & blow-dry",
        Duration = Duration.FromMinutes(45),
        Price = 40m,
        Currency = "GBP",
    };

    [Fact]
    public void Without_an_override_the_offering_is_the_service_itself()
    {
        var offering = ServiceOfferingResolver.Resolve(Cut, Anna, null);

        offering.ShouldBe(new ServiceOffering(Cut.Id, Anna, true, Duration.FromMinutes(45), 40m, "GBP", false));
    }

    [Fact]
    public void An_override_gives_that_person_their_own_duration_and_price()
    {
        var slower = new StaffServiceOverride { StaffMemberId = Anna, ServiceId = Cut.Id, Duration = Duration.FromMinutes(60), Price = 45m };

        var offering = ServiceOfferingResolver.Resolve(Cut, Anna, slower);

        offering.Offers.ShouldBeTrue();
        offering.Duration.ShouldBe(Duration.FromMinutes(60));
        offering.Price.ShouldBe(45m);
        offering.Currency.ShouldBe("GBP");
        offering.HasOverride.ShouldBeTrue();
    }

    [Fact]
    public void A_person_who_does_not_offer_the_service_is_marked_so()
    {
        var offering = ServiceOfferingResolver.Resolve(Cut, Priya, new StaffServiceOverride { StaffMemberId = Priya, ServiceId = Cut.Id, Offers = false });

        offering.Offers.ShouldBeFalse();
        offering.HasOverride.ShouldBeTrue();
    }

    [Fact]
    public void An_override_for_another_pair_is_refused()
    {
        var other = new StaffServiceOverride { StaffMemberId = Tom, ServiceId = Cut.Id, Price = 30m };

        Should.Throw<ArgumentException>(() => ServiceOfferingResolver.Resolve(Cut, Anna, other));
        Should.Throw<ArgumentException>(() => ServiceOfferingResolver.Resolve(Cut, Tom, new StaffServiceOverride { StaffMemberId = Tom, ServiceId = Guid.NewGuid(), Price = 30m }));
    }

    [Fact]
    public void The_price_span_is_the_default_when_nobody_has_an_override_or_there_is_no_staff()
    {
        ServiceOfferingResolver.PriceSpanFor(Cut, [Anna, Tom], []).ShouldBe(new PriceSpan(40m, 40m));
        ServiceOfferingResolver.PriceSpanFor(Cut, [], []).ShouldBe(new PriceSpan(40m, 40m));
        new PriceSpan(40m, 40m).IsRange.ShouldBeFalse();
    }

    [Fact]
    public void The_price_span_runs_from_the_cheapest_to_the_dearest_person_who_offers_it()
    {
        var overrides = new[]
        {
            new StaffServiceOverride { StaffMemberId = Anna, ServiceId = Cut.Id, Price = 45m },
            new StaffServiceOverride { StaffMemberId = Priya, ServiceId = Cut.Id, Price = 10m, Offers = false },
        };

        var span = ServiceOfferingResolver.PriceSpanFor(Cut, [Anna, Tom, Priya], overrides);

        span.ShouldBe(new PriceSpan(40m, 45m), "Tom at the default, Anna dearer, Priya skipped because she does not offer it");
        span.IsRange.ShouldBeTrue();
    }

    [Fact]
    public void When_everyone_who_offers_it_charges_the_same_overridden_price_that_is_the_price()
    {
        var overrides = new[]
        {
            new StaffServiceOverride { StaffMemberId = Anna, ServiceId = Cut.Id, Price = 50m },
            new StaffServiceOverride { StaffMemberId = Tom, ServiceId = Cut.Id, Price = 50m },
        };

        ServiceOfferingResolver.PriceSpanFor(Cut, [Anna, Tom], overrides).ShouldBe(new PriceSpan(50m, 50m));
    }

    [Fact]
    public void Overrides_for_other_services_or_people_outside_the_list_do_not_count()
    {
        var overrides = new[]
        {
            new StaffServiceOverride { StaffMemberId = Anna, ServiceId = Guid.NewGuid(), Price = 1m },
            new StaffServiceOverride { StaffMemberId = Priya, ServiceId = Cut.Id, Price = 99m },
        };

        ServiceOfferingResolver.PriceSpanFor(Cut, [Anna, Tom], overrides).ShouldBe(new PriceSpan(40m, 40m));
    }

    [Fact]
    public void When_nobody_in_the_list_offers_the_service_the_span_falls_back_to_the_default()
    {
        var overrides = new[] { new StaffServiceOverride { StaffMemberId = Anna, ServiceId = Cut.Id, Offers = false } };

        ServiceOfferingResolver.PriceSpanFor(Cut, [Anna], overrides).ShouldBe(new PriceSpan(40m, 40m));
    }
}