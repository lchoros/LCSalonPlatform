using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Locations;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Staff.CalendarFeedFile;

/// <summary>
/// The calendar of the staff member whose address carries <paramref name="Token"/> (ADR 0038): the visits that hold
/// their time or took place, from a month back to a year ahead. Null when this salon has no such address, or the
/// person no longer works here. A query: no transaction, tenant required.
/// </summary>
public sealed record CalendarFeedFileQuery(string Token) : IQuery<CalendarFeedFile?>;

/// <param name="CalendarName">What the calendar is called in the app that subscribes.</param>
public sealed record CalendarFeedFile(string CalendarName, IReadOnlyList<CalendarFeedEvent> Events);

/// <summary>
/// Reads through the tenant filter: a token of another salon finds nothing on this salon's host. One lookup of the
/// person, one of their visits in the window, two of the names the titles need. Health notes, phone numbers and notes
/// are never read (ADR 0011).
/// </summary>
public sealed class CalendarFeedFileHandler(
    SalonDbContext db,
    ILocationService locations,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClock clock) : IRequestHandler<CalendarFeedFileQuery, CalendarFeedFile?>
{
    public async Task<CalendarFeedFile?> HandleAsync(CalendarFeedFileQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CalendarFeedToken.LooksValid(request.Token))
        {
            return null;
        }

        var person = await db.StaffMembers.AsNoTracking()
            .Where(s => s.CalendarFeedToken == request.Token && s.IsActive)
            .Select(s => new { s.Id, s.DisplayName })
            .FirstOrDefaultAsync(cancellationToken);
        if (person is null)
        {
            return null;
        }

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(CalendarFeedFileQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");

        var now = clock.GetCurrentInstant();
        var from = CalendarFeedRules.From(now);
        var until = CalendarFeedRules.Until(now);
        var shown = CalendarFeedRules.ShownStatuses.ToArray();
        var visits = await db.Appointments.AsNoTracking()
            .Where(a => a.StaffMemberId == person.Id && a.Start >= from && a.Start < until)
            .Where(a => shown.Contains(a.Status))
            .OrderBy(a => a.Start)
            .Select(a => new { a.Id, a.Start, a.End, a.ClientId, a.ServiceId })
            .ToListAsync(cancellationToken);

        var clientIds = visits.Select(v => v.ClientId).Distinct().ToArray();
        var clients = await db.Clients.AsNoTracking()
            .Where(c => clientIds.Contains(c.Id))
            .Select(c => new { c.Id, c.FirstName, c.LastName })
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        var serviceIds = visits.Select(v => v.ServiceId).Distinct().ToArray();
        var services = await db.Services.AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var address = (await locations.GetMainAsync(cancellationToken))?.Address;

        var events = visits
            .Select(v => new CalendarFeedEvent(
                v.Id,
                v.Start,
                v.End,
                CalendarFeedRules.Summary(
                    services.GetValueOrDefault(v.ServiceId) ?? string.Empty,
                    clients.TryGetValue(v.ClientId, out var client) ? client.FirstName : string.Empty,
                    clients.TryGetValue(v.ClientId, out client) ? client.LastName : string.Empty),
                address))
            .ToArray();

        return new CalendarFeedFile($"{person.DisplayName} · {tenant.Name}", events);
    }
}