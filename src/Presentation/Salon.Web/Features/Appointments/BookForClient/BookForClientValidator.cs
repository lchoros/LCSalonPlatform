using FluentValidation;

using Microsoft.EntityFrameworkCore;

using NodaTime.Text;

using Salon.Data;
using Salon.Services.Localization;

namespace Salon.Web.Features.Appointments.BookForClient;

/// <summary>
/// Input rules for <see cref="BookForClientCommand"/>: a client, a service and a staff member are chosen, the start is an
/// ISO instant (a slot button, not a typed time) and the client belongs to this salon (the FK would otherwise fail
/// deep inside the booking). Messages come from resource keys, resolved in the request culture.
/// </summary>
public sealed class BookForClientValidator : AbstractValidator<BookForClientCommand>
{
    public BookForClientValidator(SalonDbContext db, ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.ClientId)
            .Must(id => id is { } value && value != Guid.Empty)
            .WithMessage(_ => localizer["Appointments.Book.Validation.ClientRequired"])
            .DependentRules(() =>
                RuleFor(c => c.ClientId)
                    .MustAsync(async (id, cancellationToken) => await db.Clients.AnyAsync(c => c.Id == id && c.IsActive, cancellationToken))
                    .WithMessage(_ => localizer["Appointments.Book.Validation.ClientUnknown"]));

        RuleFor(c => c.ServiceId)
            .Must(id => id is { } value && value != Guid.Empty)
            .WithMessage(_ => localizer["Appointments.Book.Validation.ServiceRequired"]);

        RuleFor(c => c.StaffMemberId)
            .Must(id => id is { } value && value != Guid.Empty)
            .WithMessage(_ => localizer["Appointments.Book.Validation.StaffRequired"]);

        RuleFor(c => c.Start)
            .Must(start => start is { Length: > 0 } && InstantPattern.ExtendedIso.Parse(start.Trim()).Success)
            .WithMessage(_ => localizer["Appointments.Book.Validation.StartInvalid"]);

        RuleFor(c => c.Date)
            .Must(date => string.IsNullOrWhiteSpace(date) || LocalDatePattern.Iso.Parse(date.Trim()).Success)
            .WithMessage(_ => localizer["Appointments.Book.Validation.DateInvalid"]);
    }
}