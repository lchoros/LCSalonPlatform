using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Paging;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Appointments.WithoutOutcome;

/// <summary>
/// The outcome service picks the visits; the page is cut first and only its rows get their client, service and staff
/// names, in three small lookups through the tenant filter. Health notes are never read here (ADR 0011).
/// </summary>
public sealed class WithoutOutcomeHandler(
    SalonDbContext db,
    IVisitOutcomeService outcomes,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup) : IRequestHandler<WithoutOutcomeQuery, Paged<VisitWithoutOutcome>>
{
    public async Task<Paged<VisitWithoutOutcome>> HandleAsync(WithoutOutcomeQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(WithoutOutcomeQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb[tenant.TimeZoneId];

        var page = Pages.Cut(await outcomes.ListMissingAsync(cancellationToken), request.Page, WithoutOutcomeQuery.PageSize);

        var clientIds = page.Items.Select(a => a.ClientId).Distinct().ToArray();
        var clients = await db.Clients.AsNoTracking()
            .Where(c => clientIds.Contains(c.Id))
            .Select(c => new { c.Id, c.FirstName, c.LastName })
            .ToDictionaryAsync(c => c.Id, c => $"{c.FirstName} {c.LastName}", cancellationToken);
        var serviceIds = page.Items.Select(a => a.ServiceId).Distinct().ToArray();
        var services = await db.Services.AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var staffIds = page.Items.Select(a => a.StaffMemberId).Distinct().ToArray();
        var staff = await db.StaffMembers.AsNoTracking()
            .Where(s => staffIds.Contains(s.Id))
            .Select(s => new { s.Id, s.DisplayName, s.Colour })
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var rows = page.Items
            .Select(a =>
            {
                var start = a.Start.InZone(zone);
                var person = staff.GetValueOrDefault(a.StaffMemberId);
                return new VisitWithoutOutcome(
                    a.Id,
                    start.Date,
                    start.TimeOfDay,
                    a.End.InZone(zone).TimeOfDay,
                    clients.GetValueOrDefault(a.ClientId, string.Empty),
                    services.GetValueOrDefault(a.ServiceId, string.Empty),
                    person?.DisplayName ?? string.Empty,
                    person?.Colour ?? string.Empty,
                    a.Status);
            })
            .ToArray();

        return new Paged<VisitWithoutOutcome>(rows, page.Page, page.PageSize, page.Total);
    }
}