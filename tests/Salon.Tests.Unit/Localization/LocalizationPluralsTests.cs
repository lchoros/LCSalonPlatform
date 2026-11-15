using System.Globalization;
using Salon.Services.Localization;
using Salon.Tests.Unit.Infrastructure;

namespace Salon.Tests.Unit.Localization;

/// <summary>Text with a count has a value for one and a value for every other number (ADR 0035).</summary>
public class LocalizationPluralsTests
{
    private static readonly InMemoryResourceSource Host = InMemoryResourceSource.Of(
        "host",
        ("en-GB", "Code.Wrong.One", "{0} attempt left."),
        ("en-GB", "Code.Wrong.Many", "{0} attempts left."),
        ("en-GB", "Merge.Moves.One", "The appointment of {1} moves to {2}."),
        ("en-GB", "Merge.Moves.Many", "All {0} appointments of {1} move to {2}."),
        ("pl-PL", "Code.Wrong.One", "Pozostała {0} próba."),
        ("pl-PL", "Code.Wrong.Many", "Pozostało prób: {0}."),
        ("pl-PL", "Merge.Moves.One", "Wizyta klienta {1} przechodzi do karty {2}."),
        ("pl-PL", "Merge.Moves.Many", "Wizyty ({0}) klienta {1} przechodzą do karty {2}."));

    private readonly LocalizationService _service = new([Host], new ListLogger<LocalizationService>());

    [Theory]
    [InlineData(1, "1 attempt left.")]
    [InlineData(0, "0 attempts left.")]
    [InlineData(2, "2 attempts left.")]
    [InlineData(21, "21 attempts left.")]
    public void Exactly_one_reads_the_one_value_and_every_other_count_the_many_value(int count, string expected)
    {
        In("en-GB", () => _service.Plural("Code.Wrong", count).ShouldBe(expected));
    }

    [Theory]
    [InlineData(1, "Pozostała 1 próba.")]
    [InlineData(2, "Pozostało prób: 2.")]
    [InlineData(5, "Pozostało prób: 5.")]
    public void A_language_with_more_forms_writes_a_many_value_that_fits_any_number(int count, string expected)
    {
        In("pl-PL", () => _service.Plural("Code.Wrong", count).ShouldBe(expected));
    }

    [Fact]
    public void The_count_is_the_first_argument_and_the_callers_arguments_follow()
    {
        In("en-GB", () =>
        {
            _service.Plural("Merge.Moves", 3, "Olivia Brown", "Olivia Browne").ShouldBe("All 3 appointments of Olivia Brown move to Olivia Browne.");
            _service.Plural("Merge.Moves", 1, "Olivia Brown", "Olivia Browne").ShouldBe("The appointment of Olivia Brown moves to Olivia Browne.");
        });
    }

    [Fact]
    public void An_empty_key_is_refused()
    {
        Should.Throw<ArgumentException>(() => _service.Plural(" ", 1));
    }

    private static void In(string culture, Action action)
    {
        var previousUi = CultureInfo.CurrentUICulture;
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.CurrentCulture = previous;
        }
    }
}