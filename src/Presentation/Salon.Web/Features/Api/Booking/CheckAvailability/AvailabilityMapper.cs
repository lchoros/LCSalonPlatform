using Salon.Api.Contracts.Booking;
using Salon.Core.Appointments;
using Salon.Web.Framework.Api;
using Salon.Web.Framework.Booking.Times;

namespace Salon.Web.Features.Api.Booking.CheckAvailability;

/// <summary>
/// Between the API's shapes and the time page's own query (ADR 0097): the request becomes the <see cref="ChooseSlotQuery"/>
/// the page sends for the same address, and its answer becomes the screen's state. Nothing is decided here.
/// </summary>
public static class AvailabilityMapper
{
    /// <summary>The query for a request the validator passed: the first service as the page's route, the rest as its <c>also</c> and <c>with</c>.</summary>
    public static ChooseSlotQuery ToQuery(AvailabilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var first = request.Parts[0];
        return new ChooseSlotQuery(
            first.ServiceId,
            ApiFormat.ParseDate(request.Date),
            Guid.TryParse(first.Staff, out var person) ? person : null,
            ApiFormat.ParseDate(request.From),
            ApiFormat.ParseInstant(request.Start),
            first.Staff == BookingApi.AnyStaff,
            [.. request.Parts.Skip(1).Select(p => p.ServiceId)],
            [.. request.Parts.Skip(1).Select(p => p.Staff)]);
    }

    /// <param name="basketOpen">Whether the salon takes another service in this booking (<c>IVisitBasket.IsOpen</c>).</param>
    /// <param name="photoUrl">The absolute address of a picture path on the salon's host.</param>
    public static Availability Map(ChooseSlotResult result, bool basketOpen, Func<string, string> photoUrl)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(photoUrl);
        var canContinue = result.Chosen is not null && !result.NeedsStaffChoice;
        return new Availability(
            [.. result.Days.Select(d => new DayItem(ApiFormat.FormatDate(d.Date), d.HasSlots))],
            ApiFormat.FormatDate(result.SelectedDate),
            result.EarlierFrom is { } earlier ? ApiFormat.FormatDate(earlier) : null,
            result.LaterFrom is { } later ? ApiFormat.FormatDate(later) : null,
            [.. result.Slots.Select(s => new TimeItem(ApiFormat.FormatInstant(s.Start), ApiFormat.FormatTime(s.LocalStart), ApiFormat.PartOfDay(s.LocalStart)))],
            result.Chosen is { } chosen ? ApiFormat.FormatInstant(chosen.Start) : null,
            Parts(result, photoUrl),
            result.Price.Amount,
            result.PriceIsFrom,
            (int)result.Duration.TotalMinutes,
            result.Price.Currency,
            canContinue,
            canContinue && basketOpen && result.Lines.Count < VisitRules.MaxServices);
    }

    /// <summary>Each service with its answer and the people to offer; once a time is chosen, each begins where the one before ends.</summary>
    private static List<VisitPartItem> Parts(ChooseSlotResult result, Func<string, string> photoUrl)
    {
        var parts = new List<VisitPartItem>();
        var next = result.Chosen?.LocalStart;
        foreach (var line in result.Lines)
        {
            var minutes = (int)line.Duration.TotalMinutes;
            var end = next?.PlusMinutes(minutes);
            parts.Add(new VisitPartItem(
                line.ServiceId,
                line.Name,
                minutes,
                line.Price.Amount,
                line.PriceIsFrom,
                line.StaffId,
                line.Anyone,
                line.NeedsStaffChoice,
                [.. line.Offered.Select(s => new StaffItem(s.Id, s.DisplayName, s.PhotoPath is { } path ? photoUrl(path) : null))],
                next is { } begins ? ApiFormat.FormatTime(begins) : null,
                end is { } ends ? ApiFormat.FormatTime(ends) : null));
            next = end;
        }

        return parts;
    }
}