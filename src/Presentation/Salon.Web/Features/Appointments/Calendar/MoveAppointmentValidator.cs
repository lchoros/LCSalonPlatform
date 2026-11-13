using FluentValidation;
using NodaTime.Text;
using Salon.Services.Localization;

namespace Salon.Web.Features.Appointments.Calendar;

/// <summary>Input rules for <see cref="MoveAppointmentCommand"/>: a staff member is chosen and the start is an ISO instant (a slot button, not a typed time).</summary>
public sealed class MoveAppointmentValidator : AbstractValidator<MoveAppointmentCommand>
{
    public MoveAppointmentValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.StaffMemberId)
            .Must(id => id is { } value && value != Guid.Empty)
            .WithMessage(_ => localizer["Appointments.Calendar.Move.Validation.StaffRequired"]);

        RuleFor(c => c.Start)
            .Must(start => start is { Length: > 0 } && InstantPattern.ExtendedIso.Parse(start.Trim()).Success)
            .WithMessage(_ => localizer["Appointments.Calendar.Move.Validation.StartInvalid"]);

        RuleFor(c => c.Date)
            .Must(date => string.IsNullOrWhiteSpace(date) || LocalDatePattern.Iso.Parse(date.Trim()).Success)
            .WithMessage(_ => localizer["Appointments.Calendar.Move.Validation.DateInvalid"]);
    }
}