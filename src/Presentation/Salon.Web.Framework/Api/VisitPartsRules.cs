using System.Linq.Expressions;
using FluentValidation;
using Salon.Api.Contracts.Booking;
using Salon.Core.Appointments;
using Salon.Services.Localization;
using Salon.Web.Framework.Booking;

namespace Salon.Web.Framework.Api;

/// <summary>
/// The services of a visit as the booking API receives them (ADR 0097): one to <see cref="VisitRules.MaxServices"/>,
/// more than one only where the salon takes several in one booking (<see cref="IVisitBasket.IsOpen"/>, ADR 0095), each
/// once, and each answer to "who with" a person's id, <see cref="BookingApi.AnyStaff"/> or, while still owed, nothing.
/// The pages read the same things leniently from an address; an app that sends them wrong has a fault to show.
/// </summary>
public static class VisitPartsRules
{
    /// <param name="answerRequired">Every service must have its answer: true when the details are posted.</param>
    public static void AddVisitPartsRules<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, IEnumerable<VisitPartRequest>>> parts,
        IVisitBasket basket,
        ILocalizationService localizer,
        bool answerRequired)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(basket);
        ArgumentNullException.ThrowIfNull(localizer);

        // A body without the list, or with a hole in it, binds as null: JSON does not hold the record to its annotations.
        validator.RuleFor(parts)
            .Cascade(CascadeMode.Stop)
            .Must(list => list is not null && list.Any())
            .WithMessage(_ => localizer["Booking.Api.Validation.PartsRequired"])
            .Must(list => list.All(p => p is not null))
            .WithMessage(_ => localizer["Booking.Api.Validation.BodyInvalid"])
            .Must(list => basket.IsOpen || list.Count() == 1)
            .WithMessage(_ => localizer["Booking.Api.Validation.OneServiceOnly"])
            .Must(list => list.Count() <= VisitRules.MaxServices)
            .WithMessage(_ => localizer.Format("Booking.Api.Validation.TooManyServices", VisitRules.MaxServices))
            .Must(list => list.Select(p => p.ServiceId).Distinct().Count() == list.Count())
            .WithMessage(_ => localizer["Booking.Api.Validation.ServiceTwice"]);

        validator.RuleForEach(parts).ChildRules(part =>
        {
            part.RuleFor(p => p.Staff)
                .Must(staff => staff is null || staff == BookingApi.AnyStaff || Guid.TryParse(staff, out _))
                .WithMessage(_ => localizer["Booking.Api.Validation.StaffInvalid"]);
            if (answerRequired)
            {
                part.RuleFor(p => p.Staff)
                    .Must(staff => !string.IsNullOrWhiteSpace(staff))
                    .WithMessage(_ => localizer["Booking.Api.Validation.StaffRequired"]);
            }
        });
    }
}