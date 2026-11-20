using FluentValidation;
using NodaTime.Text;
using Salon.Services.Localization;

namespace Salon.Web.Features.Appointments.Calendar;

/// <summary>
/// Input rules for <see cref="MoveAppointmentCommand"/>: a staff member is chosen and the new time is either an ISO
/// instant (a slot button, never a typed time) or a drop — a minute of the day together with its day (ADR 0086).
/// </summary>
public sealed class MoveAppointmentValidator : AbstractValidator<MoveAppointmentCommand>
{
    /// <summary>Minutes in a day: a drop's minute lies in [0, this).</summary>
    public const int MinutesPerDay = 24 * 60;

    public MoveAppointmentValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.StaffMemberId)
            .Must(id => id is { } value && value != Guid.Empty)
            .WithMessage(_ => localizer["Appointments.Calendar.Move.Validation.StaffRequired"]);

        RuleFor(c => c.Start)
            .Must(start => start is { Length: > 0 } && InstantPattern.ExtendedIso.Parse(start.Trim()).Success)
            .When(c => c.Minute is null)
            .WithMessage(_ => localizer["Appointments.Calendar.Move.Validation.StartInvalid"]);

        RuleFor(c => c.Minute)
            .Must(minute => minute is >= 0 and < MinutesPerDay)
            .When(c => c.Minute is not null)
            .WithMessage(_ => localizer["Appointments.Calendar.Move.Validation.StartInvalid"]);

        RuleFor(c => c.Date)
            .Must(date => string.IsNullOrWhiteSpace(date) || LocalDatePattern.Iso.Parse(date.Trim()).Success)
            .WithMessage(_ => localizer["Appointments.Calendar.Move.Validation.DateInvalid"]);

        // A drop without its day cannot be placed on the salon's clock.
        RuleFor(c => c.Date)
            .Must(date => !string.IsNullOrWhiteSpace(date))
            .When(c => c.Minute is not null)
            .WithMessage(_ => localizer["Appointments.Calendar.Move.Validation.DateInvalid"]);
    }
}