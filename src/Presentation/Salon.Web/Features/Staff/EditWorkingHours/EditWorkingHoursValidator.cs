using FluentValidation;
using Salon.Core.Staff;
using Salon.Services.Localization;

namespace Salon.Web.Features.Staff.EditWorkingHours;

/// <summary>
/// Rules for <see cref="EditWorkingHoursCommand"/>: seven distinct days; on a working day each used slot has both times,
/// parseable, on the 5-minute grid, end after start; at most two slots; no overlap between the day's valid ranges. A day
/// marked off is not checked at all. Failures carry the form's property paths (<c>Days[1].Ranges[0].End</c>,
/// <c>Days[1].Ranges</c>) so each message lands next to its field. Messages come from resource keys in the request culture.
/// </summary>
public sealed class EditWorkingHoursValidator : AbstractValidator<EditWorkingHoursCommand>
{
    public EditWorkingHoursValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Days)
            .Must(days => days is not null && days.Count == WorkingHoursRules.Week.Count && days.Select(d => d.Day).Distinct().Count() == WorkingHoursRules.Week.Count)
            .WithMessage(_ => localizer["Staff.Hours.Validation.DaysInvalid"]);

        RuleForEach(c => c.Days)
            .SetValidator(new DayHoursValidator(localizer))
            .When(c => c.Days is not null);
    }

    private sealed class DayHoursValidator : AbstractValidator<DayHoursInput>
    {
        public DayHoursValidator(ILocalizationService localizer)
        {
            RuleFor(d => d.Ranges)
                .Must(ranges => ranges is not null && ranges.Count <= WorkingHoursRules.MaxRangesPerDay)
                .WithMessage(_ => localizer.Format("Staff.Hours.Validation.TooManyRanges", WorkingHoursRules.MaxRangesPerDay))
                .Must(ranges => ranges is null || WorkingHoursRules.OverlappingIndexes(ValidRanges(ranges)).Count == 0)
                .WithMessage(_ => localizer["Staff.Hours.Validation.Overlap"])
                .When(d => !d.IsDayOff);

            RuleForEach(d => d.Ranges)
                .SetValidator(new TimeRangeValidator(localizer))
                .When(d => !d.IsDayOff && d.Ranges is not null);
        }

        /// <summary>Only ranges that already pass the per-slot rules take part in the overlap check, so a typo is reported once.</summary>
        private static List<TimeRange> ValidRanges(IReadOnlyList<TimeRangeInput> ranges)
            => ranges.Select(r => r.Parsed).OfType<TimeRange>().Where(WorkingHoursRules.EndsAfterStart).ToList();
    }

    private sealed class TimeRangeValidator : AbstractValidator<TimeRangeInput>
    {
        public TimeRangeValidator(ILocalizationService localizer)
        {
            RuleFor(r => r.Start)
                .Cascade(CascadeMode.Stop)
                .Must(start => !string.IsNullOrWhiteSpace(start))
                .WithMessage(_ => localizer["Staff.Hours.Validation.StartRequired"])
                .Must(start => WorkingHoursRules.TryParseTime(start) is not null)
                .WithMessage(_ => localizer["Staff.Hours.Validation.TimeInvalid"])
                .Must(start => WorkingHoursRules.IsOnStep(WorkingHoursRules.TryParseTime(start)!.Value))
                .WithMessage(_ => localizer.Format("Staff.Hours.Validation.Step", WorkingHoursRules.StepMinutes))
                .When(r => !r.IsBlank);

            RuleFor(r => r.End)
                .Cascade(CascadeMode.Stop)
                .Must(end => !string.IsNullOrWhiteSpace(end))
                .WithMessage(_ => localizer["Staff.Hours.Validation.EndRequired"])
                .Must(end => WorkingHoursRules.TryParseTime(end) is not null)
                .WithMessage(_ => localizer["Staff.Hours.Validation.TimeInvalid"])
                .Must(end => WorkingHoursRules.IsOnStep(WorkingHoursRules.TryParseTime(end)!.Value))
                .WithMessage(_ => localizer.Format("Staff.Hours.Validation.Step", WorkingHoursRules.StepMinutes))
                .Must((range, _) => range.Parsed is not { } parsed || WorkingHoursRules.EndsAfterStart(parsed))
                .WithMessage(_ => localizer["Staff.Hours.Validation.EndAfterStart"])
                .When(r => !r.IsBlank);
        }
    }
}