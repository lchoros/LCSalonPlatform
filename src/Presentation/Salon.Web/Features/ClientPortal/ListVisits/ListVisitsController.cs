using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.ClientPortal;
using Salon.Services.Formatting;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.ClientPortal;
using Salon.Web.Framework.Controllers;

namespace Salon.Web.Features.ClientPortal.ListVisits;

/// <summary>
/// <c>/my-bookings</c> (ADR 0083): the visits still ahead of the client who proved their number, each with the links
/// to the change and cancel pages that already exist (ADR 0014, ADR 0021). Only the client's own cookie opens it; a
/// staff login does not. Without a session that stands, the visitor is sent to the sign-in. The page is never cached.
/// </summary>
[ClientPortalGate]
public sealed class ListVisitsController(
    IDispatcher dispatcher,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClientPortalSession session) : SalonControllerBase
{
    [HttpGet(ClientPortalDefaults.VisitsPath)]
    public async Task<IActionResult> Visits(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        // The gate has already turned a request without a salon into a 404.
        var tenantId = tenantContext.TenantId!.Value;
        var current = session.Read(Request, tenantId);
        var visits = current is null
            ? null
            : await dispatcher.SendAsync(new UpcomingVisitsQuery(current.ClientId, current.IssuedAt), cancellationToken);
        if (current is null || visits is null)
        {
            // Expired, another salon's, forged, or the card behind it has changed: the cookie goes and the client starts again.
            if (Request.Cookies.ContainsKey(ClientPortalDefaults.CookieName))
            {
                session.Clear(Response);
            }

            return Redirect(ClientPortalDefaults.SignInPath);
        }

        session.Slide(Response, current);

        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken);
        var zone = (tenant is null ? null : DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId)) ?? DateTimeZone.Utc;
        var culture = CultureInfo.CurrentCulture;
        return View(new ListVisitsViewModel(
            tenant?.Name ?? string.Empty,
            [.. visits.Select(v => RowFor(v, zone, culture))],
            PublicBookingDefaults.BookPath,
            ClientPortalDefaults.SignOutPath));
    }

    private static VisitRow RowFor(ClientPortalVisit visit, DateTimeZone zone, CultureInfo culture)
    {
        var local = visit.Start.InZone(zone).LocalDateTime;
        return new VisitRow(
            visit.ServiceName,
            visit.StaffName,
            local.Date.ToDayHeading(culture),
            local.TimeOfDay.ToClockTime(),
            visit.Start.ToIso(),
            visit.PublicToken is { } change ? PublicBookingDefaults.ChangePath(change) : null,
            visit.PublicToken is { } cancel ? PublicBookingDefaults.CancelPath(cancel) : null);
    }
}

/// <param name="Day">The day in the salon's zone and the reader's language, e.g. "Tuesday 24 November 2026".</param>
/// <param name="Time">The time in the salon's zone, e.g. "10:30".</param>
/// <param name="StartIso">The instant, for the <c>time</c> element.</param>
/// <param name="ChangePath">The client's own change page; null for a visit that has no link (booked at the desk long ago).</param>
/// <param name="CancelPath">The client's own cancel page; null likewise.</param>
public sealed record VisitRow(string ServiceName, string StaffName, string Day, string Time, string StartIso, string? ChangePath, string? CancelPath);

public sealed record ListVisitsViewModel(string SalonName, IReadOnlyList<VisitRow> Visits, string BookPath, string SignOutPath);