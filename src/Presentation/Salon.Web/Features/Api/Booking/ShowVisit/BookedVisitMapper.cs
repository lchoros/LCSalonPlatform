using System.Text.Json;
using NodaTime;
using Salon.Api.Contracts.Booking;
using Salon.Services.Booking;
using Salon.Web.Framework.Api;

namespace Salon.Web.Features.Api.Booking.ShowVisit;

/// <summary>A booked visit as the app's confirmation: instants in UTC, the day and the time in the salon's zone.</summary>
public static class BookedVisitMapper
{
    /// <param name="manageUrl">The client's own link on the salon's host, the one the confirmation text carries.</param>
    public static BookedVisit Map(PublicAppointment visit, DateTimeZone zone, string manageUrl)
    {
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(zone);
        var local = visit.Start.InZone(zone);
        // "booked", "cancelledByClient", ...: the status's own name, in the casing of the rest of the JSON.
        var status = JsonNamingPolicy.CamelCase.ConvertName(visit.Status.ToString());
        return new BookedVisit(
            visit.Id,
            visit.ServiceName,
            visit.StaffName,
            ApiFormat.FormatInstant(visit.Start),
            ApiFormat.FormatInstant(visit.End),
            ApiFormat.FormatDate(local.Date),
            ApiFormat.FormatTime(local.TimeOfDay),
            visit.Price.Amount,
            visit.Price.Currency,
            status,
            manageUrl);
    }
}