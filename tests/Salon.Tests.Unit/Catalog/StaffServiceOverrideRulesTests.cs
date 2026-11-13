using NodaTime;
using Salon.Core.Catalog;

namespace Salon.Tests.Unit.Catalog;

/// <summary>
/// The pure override rules of ADR 0018: an override wins over the service default for duration and price, a missing or
/// blank override falls back to the service, "does not offer" is only ever stated explicitly, an all-default row is
/// meaningless (and gets deleted), and an override duration or price obeys the same bounds as the service itself.
/// </summary>
public class StaffServiceOverrideRulesTests
{
    private static readonly Service Cut = new()
    {
        Name = "Cut & blow-dry",
        Duration = Duration.FromMinutes(45),
        Price = 40m,
        Currency = "GBP",
    };

    [Fact]
    public void Without_an_override_the_service_defaults_apply_and_the_service_is_offered()
    {
        ServiceRules.EffectiveDuration(Cut, null).ShouldBe(Duration.FromMinutes(45));
        ServiceRules.EffectivePrice(Cut, null).ShouldBe(40m);
        ServiceRules.IsOffered(null).ShouldBeTrue();
    }

    [Fact]
    public void An_override_wins_for_the_fields_it_sets_and_falls_back_for_the_rest()
    {
        var slowerOnly = new StaffServiceOverride { StaffMemberId = Guid.NewGuid(), ServiceId = Cut.Id, Duration = Duration.FromMinutes(60) };
        var dearerOnly = new StaffServiceOverride { StaffMemberId = Guid.NewGuid(), ServiceId = Cut.Id, Price = 45m };
        var both = new StaffServiceOverride { StaffMemberId = Guid.NewGuid(), ServiceId = Cut.Id, Duration = Duration.FromMinutes(60), Price = 45m };

        ServiceRules.EffectiveDuration(Cut, slowerOnly).ShouldBe(Duration.FromMinutes(60));
        ServiceRules.EffectivePrice(Cut, slowerOnly).ShouldBe(40m);

        ServiceRules.EffectiveDuration(Cut, dearerOnly).ShouldBe(Duration.FromMinutes(45));
        ServiceRules.EffectivePrice(Cut, dearerOnly).ShouldBe(45m);

        ServiceRules.EffectiveDuration(Cut, both).ShouldBe(Duration.FromMinutes(60));
        ServiceRules.EffectivePrice(Cut, both).ShouldBe(45m);
    }

    [Fact]
    public void Not_offering_is_explicit_and_keeps_the_effective_values_readable()
    {
        var notOffered = new StaffServiceOverride { StaffMemberId = Guid.NewGuid(), ServiceId = Cut.Id, Offers = false };

        ServiceRules.IsOffered(notOffered).ShouldBeFalse();
        ServiceRules.IsOffered(new StaffServiceOverride { StaffMemberId = Guid.NewGuid(), ServiceId = Cut.Id }).ShouldBeTrue("Offers defaults to true");
        ServiceRules.EffectiveDuration(Cut, notOffered).ShouldBe(Cut.Duration);
        ServiceRules.EffectivePrice(Cut, notOffered).ShouldBe(Cut.Price);
    }

    [Fact]
    public void A_row_that_changes_nothing_is_the_default_and_need_not_be_stored()
    {
        new StaffServiceOverride { StaffMemberId = Guid.NewGuid(), ServiceId = Cut.Id }.IsDefault.ShouldBeTrue();
        new StaffServiceOverride { StaffMemberId = Guid.NewGuid(), ServiceId = Cut.Id, Offers = false }.IsDefault.ShouldBeFalse();
        new StaffServiceOverride { StaffMemberId = Guid.NewGuid(), ServiceId = Cut.Id, Duration = Duration.FromMinutes(30) }.IsDefault.ShouldBeFalse();
        new StaffServiceOverride { StaffMemberId = Guid.NewGuid(), ServiceId = Cut.Id, Price = 0m }.IsDefault.ShouldBeFalse("a free service for one person is a real override");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(5, true)]
    [InlineData(60, true)]
    [InlineData(600, true)]
    [InlineData(0, false)]
    [InlineData(4, false)]
    [InlineData(47, false)]
    [InlineData(605, false)]
    public void An_override_duration_is_blank_or_on_the_grid_within_the_service_bounds(int? minutes, bool valid)
    {
        ServiceRules.IsOverrideDurationValid(minutes).ShouldBe(valid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("0", true)]
    [InlineData("45.50", true)]
    [InlineData("100000", true)]
    [InlineData("-0.01", false)]
    [InlineData("45.005", false)]
    [InlineData("100000.01", false)]
    public void An_override_price_is_blank_or_a_two_decimal_amount_from_zero_to_the_cap(string? price, bool valid)
    {
        decimal? value = price is null ? null : decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);
        ServiceRules.IsOverridePriceValid(value).ShouldBe(valid);
    }
}