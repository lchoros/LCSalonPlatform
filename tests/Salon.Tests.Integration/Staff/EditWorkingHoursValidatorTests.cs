using System.Globalization;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Staff;
using Salon.Services;
using Salon.Services.Localization;
using Salon.Web.Features.Staff.EditWorkingHours;

namespace Salon.Tests.Integration.Staff;

/// <summary>
/// The rota rules as the form sees them, with the host's real resources in both cultures: both ends of a used slot,
/// parseable, on the 5-minute grid, end after start, at most two slots, no overlap; a day off is never checked; failures
/// carry the form's property paths. Pure (no database, no host); here because only this project references Salon.Web.
/// </summary>
public class EditWorkingHoursValidatorTests
{
    private static readonly ILocalizationService Localizer = new ServiceCollection()
        .AddLogging()
        .AddSalonServices()
        .AddLocalizationResources(typeof(Program).Assembly)
        .BuildServiceProvider()
        .GetRequiredService<ILocalizationService>();

    [Fact]
    public void A_full_week_with_split_shifts_and_days_off_passes()
    {
        var command = Week(
            (IsoDayOfWeek.Monday, false, [("09:00", "13:00"), ("14:00", "17:00")]),
            (IsoDayOfWeek.Saturday, true, [("09:00", "nonsense")]),
            (IsoDayOfWeek.Sunday, false, [("", ""), ("", "")]));

        Validate(command, "en-GB").IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Touching_ranges_are_fine_and_so_is_a_second_slot_before_the_first()
    {
        Validate(Week((IsoDayOfWeek.Monday, false, [("09:00", "13:00"), ("13:00", "17:00")])), "en-GB").IsValid.ShouldBeTrue();
        Validate(Week((IsoDayOfWeek.Monday, false, [("14:00", "17:00"), ("09:00", "13:00")])), "en-GB").IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("en-GB", "The form must list each day of the week once.")]
    [InlineData("pl-PL", "Formularz musi zawierać każdy dzień tygodnia dokładnie raz.")]
    public void Every_day_must_appear_exactly_once(string culture, string message)
    {
        var sixDays = new EditWorkingHoursCommand(Guid.NewGuid(), Week().Days.Take(6).ToList());
        var twoMondays = new EditWorkingHoursCommand(Guid.NewGuid(), Week().Days.Take(6).Append(Week().Days[0]).ToList());

        foreach (var command in new[] { sixDays, twoMondays })
        {
            var result = Validate(command, culture);

            result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("Days");
            result.Errors[0].ErrorMessage.ShouldBe(message);
        }
    }

    [Theory]
    [InlineData("en-GB", "Enter the start time.", "Enter the end time.")]
    [InlineData("pl-PL", "Podaj godzinę rozpoczęcia.", "Podaj godzinę zakończenia.")]
    public void A_half_filled_slot_asks_for_the_missing_end(string culture, string startMessage, string endMessage)
    {
        var result = Validate(Week((IsoDayOfWeek.Tuesday, false, [("", "17:00"), ("09:00", "")])), culture);

        result.Errors.Select(e => (e.PropertyName, e.ErrorMessage)).ShouldBe(
        [
            ("Days[1].Ranges[0].Start", startMessage),
            ("Days[1].Ranges[1].End", endMessage),
        ]);
    }

    [Theory]
    [InlineData("en-GB", "Enter a time as HH:MM.")]
    [InlineData("pl-PL", "Podaj godzinę w formacie GG:MM.")]
    public void Text_that_is_not_a_time_is_reported_once_per_field(string culture, string message)
    {
        var result = Validate(Week((IsoDayOfWeek.Monday, false, [("9am", "5pm")])), culture);

        result.Errors.Select(e => (e.PropertyName, e.ErrorMessage)).ShouldBe(
        [
            ("Days[0].Ranges[0].Start", message),
            ("Days[0].Ranges[0].End", message),
        ]);
    }

    [Theory]
    [InlineData("en-GB", "Times must be in 5-minute steps.")]
    [InlineData("pl-PL", "Godziny muszą być wielokrotnością 5 minut.")]
    public void Times_off_the_five_minute_grid_fail(string culture, string message)
    {
        var result = Validate(Week((IsoDayOfWeek.Monday, false, [("09:03", "17:00")])), culture);

        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("Days[0].Ranges[0].Start");
        result.Errors[0].ErrorMessage.ShouldBe(message);
    }

    [Theory]
    [InlineData("en-GB", "The end time must be after the start time.")]
    [InlineData("pl-PL", "Godzina zakończenia musi być późniejsza niż godzina rozpoczęcia.")]
    public void The_end_must_come_after_the_start(string culture, string message)
    {
        foreach (var range in new[] { ("17:00", "09:00"), ("09:00", "09:00") })
        {
            var result = Validate(Week((IsoDayOfWeek.Friday, false, [range])), culture);

            result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("Days[4].Ranges[0].End");
            result.Errors[0].ErrorMessage.ShouldBe(message);
        }
    }

    [Theory]
    [InlineData("en-GB", "The ranges on this day overlap.")]
    [InlineData("pl-PL", "Przedziały w tym dniu nakładają się.")]
    public void Overlapping_ranges_are_reported_on_the_day(string culture, string message)
    {
        var result = Validate(Week((IsoDayOfWeek.Wednesday, false, [("09:00", "13:05"), ("13:00", "17:00")])), culture);

        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("Days[2].Ranges");
        result.Errors[0].ErrorMessage.ShouldBe(message);
    }

    [Fact]
    public void A_broken_range_is_not_also_reported_as_an_overlap()
    {
        var result = Validate(Week((IsoDayOfWeek.Monday, false, [("09:00", "17:00"), ("10:00", "09:00")])), "en-GB");

        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("Days[0].Ranges[1].End");
    }

    [Theory]
    [InlineData("en-GB", "At most 2 ranges per day.")]
    [InlineData("pl-PL", "Najwyżej 2 przedziały dziennie.")]
    public void More_than_two_slots_on_a_day_fail(string culture, string message)
    {
        var result = Validate(Week((IsoDayOfWeek.Monday, false, [("08:00", "10:00"), ("11:00", "12:00"), ("13:00", "14:00")])), culture);

        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("Days[0].Ranges");
        result.Errors[0].ErrorMessage.ShouldBe(message);
    }

    [Fact]
    public void A_day_off_is_not_validated_at_all()
    {
        var command = Week((IsoDayOfWeek.Monday, true, [("junk", ""), ("17:00", "09:00"), ("09:00", "10:00")]));

        Validate(command, "pl-PL").IsValid.ShouldBeTrue();
    }

    /// <summary>Seven days, each blank (a day off by emptiness) unless overridden.</summary>
    private static EditWorkingHoursCommand Week(params (IsoDayOfWeek Day, bool Off, (string Start, string End)[] Ranges)[] overrides)
    {
        var days = WorkingHoursRules.Week.Select(day =>
        {
            var match = overrides.FirstOrDefault(o => o.Day == day);
            var ranges = match.Ranges ?? [("", ""), ("", "")];
            return new DayHoursInput(day, match.Off, ranges.Select(r => new TimeRangeInput(r.Start, r.End)).ToList());
        }).ToList();

        return new EditWorkingHoursCommand(Guid.NewGuid(), days);
    }

    private static ValidationResult Validate(EditWorkingHoursCommand command, string culture)
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        var requested = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentCulture = requested;
        CultureInfo.CurrentUICulture = requested;
        try
        {
            return new EditWorkingHoursValidator(Localizer).Validate(command);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
        }
    }
}