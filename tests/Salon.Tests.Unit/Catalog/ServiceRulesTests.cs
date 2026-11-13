using System.Globalization;
using Salon.Core.Catalog;

namespace Salon.Tests.Unit.Catalog;

/// <summary>
/// The pure catalogue rules of ADR 0013, shared by the Create and Edit slices: name trimming and limits, the 5-minute grid,
/// the duration/buffer/processing ranges, the processing window inside the duration, price shape, and literal LIKE matching.
/// </summary>
public class ServiceRulesTests
{
    [Theory]
    [InlineData("  Cut & blow-dry  ", "Cut & blow-dry")]
    [InlineData("Balayage", "Balayage")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void A_name_is_trimmed_and_never_null(string? input, string expected)
    {
        ServiceRules.NormalizeName(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_description_becomes_null(string? input)
    {
        ServiceRules.NormalizeDescription(input).ShouldBeNull();
    }

    [Fact]
    public void A_description_is_trimmed()
    {
        ServiceRules.NormalizeDescription("  Wash, cut and finish.  ").ShouldBe("Wash, cut and finish.");
    }

    [Fact]
    public void A_name_is_required_and_measured_after_trimming()
    {
        ServiceRules.HasName("Cut").ShouldBeTrue();
        ServiceRules.HasName("   ").ShouldBeFalse();
        ServiceRules.HasName(null).ShouldBeFalse();

        ServiceRules.NameFits("  " + new string('a', ServiceRules.NameMaxLength) + "  ").ShouldBeTrue();
        ServiceRules.NameFits(new string('a', ServiceRules.NameMaxLength + 1)).ShouldBeFalse();
        ServiceRules.NameFits(null).ShouldBeTrue("absence is reported by HasName, not by the length rule");
    }

    [Fact]
    public void A_category_name_is_shorter_than_a_service_name()
    {
        ServiceRules.CategoryNameFits(new string('a', ServiceRules.CategoryNameMaxLength)).ShouldBeTrue();
        ServiceRules.CategoryNameFits(new string('a', ServiceRules.CategoryNameMaxLength + 1)).ShouldBeFalse();
        ServiceRules.CategoryNameMaxLength.ShouldBe(ServiceCategory.NameMaxLength);
        ServiceRules.CategoryNameMaxLength.ShouldBe(100);
    }

    [Fact]
    public void A_description_is_optional_but_capped()
    {
        ServiceRules.DescriptionFits(null).ShouldBeTrue();
        ServiceRules.DescriptionFits(new string('x', ServiceRules.DescriptionMaxLength)).ShouldBeTrue();
        ServiceRules.DescriptionFits(new string('x', ServiceRules.DescriptionMaxLength + 1)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(45, true)]
    [InlineData(240, true)]
    [InlineData(3, false)]
    [InlineData(47, false)]
    [InlineData(-5, true)]
    public void Minutes_sit_on_a_five_minute_grid(int minutes, bool onStep)
    {
        ServiceRules.IsOnStep(minutes).ShouldBe(onStep);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(600, true)]
    [InlineData(0, false)]
    [InlineData(4, false)]
    [InlineData(605, false)]
    public void A_duration_runs_from_5_to_600_minutes(int minutes, bool inRange)
    {
        ServiceRules.IsDurationInRange(minutes).ShouldBe(inRange);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(15, true)]
    [InlineData(120, true)]
    [InlineData(-5, false)]
    [InlineData(125, false)]
    public void A_buffer_runs_from_0_to_120_minutes(int minutes, bool inRange)
    {
        ServiceRules.IsBufferInRange(minutes).ShouldBe(inRange);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(240, true)]
    [InlineData(-5, false)]
    [InlineData(245, false)]
    public void Processing_time_runs_from_0_to_240_minutes(int minutes, bool inRange)
    {
        ServiceRules.IsProcessingInRange(minutes).ShouldBe(inRange);
    }

    [Theory]
    [InlineData(90, 30, 20, true)]
    [InlineData(90, 30, 60, true)]
    [InlineData(90, 0, 0, true)]
    [InlineData(90, 0, 90, true)]
    [InlineData(90, 30, 65, false)]
    [InlineData(90, 90, 5, false)]
    [InlineData(90, 100, 0, false)]
    [InlineData(90, 30, -5, false)]
    public void The_processing_window_must_lie_inside_the_duration(int duration, int processing, int startsAfter, bool fits)
    {
        ServiceRules.ProcessingFitsInside(duration, processing, startsAfter).ShouldBe(fits);
    }

    [Fact]
    public void Without_processing_time_the_offset_is_meaningless_and_normalises_to_zero()
    {
        ServiceRules.NormalizeProcessingStart(processingMinutes: 0, processingStartsAfterMinutes: 20).ShouldBe(0);
        ServiceRules.NormalizeProcessingStart(processingMinutes: 30, processingStartsAfterMinutes: 20).ShouldBe(20);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("45.50", true)]
    [InlineData("100000", true)]
    [InlineData("-0.01", false)]
    [InlineData("100000.01", false)]
    public void A_price_runs_from_zero_to_the_cap(string price, bool inRange)
    {
        ServiceRules.IsPriceInRange(decimal.Parse(price, CultureInfo.InvariantCulture)).ShouldBe(inRange);
    }

    [Theory]
    [InlineData("45", true)]
    [InlineData("45.5", true)]
    [InlineData("45.50", true)]
    [InlineData("45.005", false)]
    public void A_price_has_at_most_two_decimals(string price, bool ok)
    {
        ServiceRules.HasAtMostTwoDecimals(decimal.Parse(price, CultureInfo.InvariantCulture)).ShouldBe(ok);
    }

    [Theory]
    [InlineData("Cut & blow-dry", "cut & blow-dry", true)]
    [InlineData("  Cut  ", "cut", true)]
    [InlineData("Cut", "Cut ", true)]
    [InlineData("Cut", "Cuts", false)]
    public void Names_match_case_insensitively_after_trimming(string a, string b, bool same)
    {
        ServiceRules.NamesMatch(a, b).ShouldBe(same);
    }

    [Theory]
    [InlineData("Cut", "Cut")]
    [InlineData("100% blonde", "100\\% blonde")]
    [InlineData("under_score", "under\\_score")]
    [InlineData("back\\slash", "back\\\\slash")]
    public void A_name_is_escaped_so_LIKE_matches_it_literally(string name, string pattern)
    {
        ServiceRules.LikeLiteral(name).ShouldBe(pattern);
    }

    [Fact]
    public void The_limits_are_as_decided()
    {
        ServiceRules.NameMaxLength.ShouldBe(200);
        ServiceRules.DescriptionMaxLength.ShouldBe(2000);
        ServiceRules.StepMinutes.ShouldBe(5);
        ServiceRules.MinDurationMinutes.ShouldBe(5);
        ServiceRules.MaxDurationMinutes.ShouldBe(600);
        ServiceRules.MaxBufferAfterMinutes.ShouldBe(120);
        ServiceRules.MaxProcessingMinutes.ShouldBe(240);
        ServiceRules.MaxPrice.ShouldBe(100_000m);
    }
}