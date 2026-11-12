using FluentValidation;

using Microsoft.EntityFrameworkCore;

using Salon.Core.Staff;
using Salon.Data;
using Salon.Services.Localization;

namespace Salon.Web.Features.Staff.TimeOff;

/// <summary>
/// Rules for <see cref="AddTimeOffCommand"/> (ADR 0015): both dates present and parseable, the span in order and at most
/// <see cref="StaffTimeOffRules.MaxSpanDays"/> days; times either both blank or both present, parseable, on the 5-minute
/// grid, end after start; the reason within its limit; and no overlap with an existing entry of the same person (checked
/// only once the rest is valid, so a typo is reported once). Failures carry the form's field names; the overlap is a
/// form-level message. Messages come from resource keys in the request culture.
/// </summary>
public sealed class AddTimeOffValidator : AbstractValidator<AddTimeOffCommand>
{
    /// <summary>Model-state key of the overlap failure: it belongs to the entry as a whole, so the page shows it above the fields.</summary>
    public const string OverlapKey = "Overlap";

    public AddTimeOffValidator(SalonDbContext db, ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.FromDate)
            .Cascade(CascadeMode.Stop)
            .Must(from => !string.IsNullOrWhiteSpace(from))
            .WithMessage(_ => localizer["Staff.TimeOff.Validation.FromRequired"])
            .Must(from => StaffTimeOffRules.TryParseDate(from) is not null)
            .WithMessage(_ => localizer["Staff.TimeOff.Validation.DateInvalid"]);

        RuleFor(c => c.ToDate)
            .Cascade(CascadeMode.Stop)
            .Must(to => !string.IsNullOrWhiteSpace(to))
            .WithMessage(_ => localizer["Staff.TimeOff.Validation.ToRequired"])
            .Must(to => StaffTimeOffRules.TryParseDate(to) is not null)
            .WithMessage(_ => localizer["Staff.TimeOff.Validation.DateInvalid"])
            .Must((c, _) => c.ParsedFrom is not { } from || StaffTimeOffRules.EndsOnOrAfterStart(from, c.ParsedTo!.Value))
            .WithMessage(_ => localizer["Staff.TimeOff.Validation.ToBeforeFrom"])
            .Must((c, _) => c.ParsedFrom is not { } from || StaffTimeOffRules.FitsMaxSpan(from, c.ParsedTo!.Value))
            .WithMessage(_ => localizer.Format("Staff.TimeOff.Validation.SpanTooLong", StaffTimeOffRules.MaxSpanDays));

        RuleFor(c => c.Start)
            .Cascade(CascadeMode.Stop)
            .Must((c, _) => c.HasStart || !c.HasEnd)
            .WithMessage(_ => localizer["Staff.TimeOff.Validation.TimesIncomplete"])
            .Must((c, start) => !c.HasStart || WorkingHoursRules.TryParseTime(start) is not null)
            .WithMessage(_ => localizer["Staff.TimeOff.Validation.TimeInvalid"])
            .Must((c, start) => !c.HasStart || WorkingHoursRules.IsOnStep(WorkingHoursRules.TryParseTime(start)!.Value))
            .WithMessage(_ => localizer.Format("Staff.TimeOff.Validation.Step", WorkingHoursRules.StepMinutes));

        RuleFor(c => c.End)
            .Cascade(CascadeMode.Stop)
            .Must((c, _) => c.HasEnd || !c.HasStart)
            .WithMessage(_ => localizer["Staff.TimeOff.Validation.TimesIncomplete"])
            .Must((c, end) => !c.HasEnd || WorkingHoursRules.TryParseTime(end) is not null)
            .WithMessage(_ => localizer["Staff.TimeOff.Validation.TimeInvalid"])
            .Must((c, end) => !c.HasEnd || WorkingHoursRules.IsOnStep(WorkingHoursRules.TryParseTime(end)!.Value))
            .WithMessage(_ => localizer.Format("Staff.TimeOff.Validation.Step", WorkingHoursRules.StepMinutes))
            .Must((c, _) => c.ParsedStart is not { } start || c.ParsedEnd is not { } end || WorkingHoursRules.EndsAfterStart(new TimeRange(start, end)))
            .WithMessage(_ => localizer["Staff.TimeOff.Validation.EndAfterStart"]);

        RuleFor(c => c.Reason)
            .Must(StaffTimeOffRules.ReasonFits)
            .WithMessage(_ => localizer.Format("Staff.TimeOff.Validation.ReasonTooLong", StaffTimeOff.ReasonMaxLength));

        // Only a fully valid entry is compared with the person's existing ones; the rules above explain anything else first.
        RuleFor(c => c)
            .MustAsync(async (c, cancellationToken) =>
            {
                var entry = c.ToEntry();
                if (entry is null || StaffTimeOffRules.Problems(entry.FromDate, entry.ToDate, entry.Start, entry.End, entry.Reason).Count > 0)
                {
                    return true;
                }

                var existing = await db.StaffTimeOff.AsNoTracking()
                    .Where(t => t.StaffMemberId == entry.StaffMemberId && t.FromDate <= entry.ToDate && t.ToDate >= entry.FromDate)
                    .ToListAsync(cancellationToken);
                return !existing.Any(other => StaffTimeOffRules.Overlaps(entry, other));
            })
            .OverridePropertyName(OverlapKey)
            .WithMessage(_ => localizer["Staff.TimeOff.Validation.Overlap"]);
    }
}