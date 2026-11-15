using Microsoft.AspNetCore.Mvc;
using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Staff;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Staff.CalendarFeedFile;

/// <summary>
/// <c>GET /calendar/{token}.ics</c> (ADR 0038): a staff member's visits as an iCalendar file, for Google Calendar,
/// Apple Calendar or Outlook to subscribe to. Tenant only and without a sign-in, because a calendar app cannot sign
/// in: the long random token in the address is the credential. An address this salon does not know is a plain 404,
/// with no hint whether it ever existed. The answer is never to be kept by a shared cache or shown by a search engine.
/// </summary>
[RequireTenant]
[Route("calendar")]
public sealed class CalendarFeedFileController(IDispatcher dispatcher, IClock clock) : SalonControllerBase
{
    private const string Extension = ".ics";

    [HttpGet("{file}")]
    public async Task<IActionResult> Get(string file, CancellationToken cancellationToken)
    {
        if (file is null || !file.EndsWith(Extension, StringComparison.Ordinal))
        {
            return NotFound();
        }

        var feed = await dispatcher.SendAsync(new CalendarFeedFileQuery(file[..^Extension.Length]), cancellationToken);
        if (feed is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        return Content(CalendarFeedWriter.Write(feed.CalendarName, Request.Host.Host, feed.Events, clock.GetCurrentInstant()), CalendarFeedWriter.ContentType);
    }
}