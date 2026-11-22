using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Tenancy;

namespace Salon.Services.Notifications;

/// <summary>
/// Everything a message about one appointment needs, read once for the salon in scope: the salon's name, zone and
/// culture, the client's first name, phone and email, the service and the person. Used by the message consumers and the
/// reminders so that each of them is only its own rule (ADR 0021, ADR 0025).
/// </summary>
public sealed class AppointmentMessageReader(SalonDbContext db, ITenantContext tenantContext, ITenantLookup tenantLookup)
{
    /// <summary>Null when the appointment does not exist in the salon in scope.</summary>
    public async Task<AppointmentMessageData?> ReadAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("A message about an appointment needs its salon in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");

        var appointment = await db.Appointments.AsNoTracking()
            .Where(a => a.Id == appointmentId)
            .Select(a => new { a.Id, a.ClientId, a.ServiceId, a.StaffMemberId, a.Start, a.Status, a.PublicToken, a.VisitGroupId })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (appointment is null)
        {
            return null;
        }

        var client = await db.Clients.AsNoTracking()
            .Where(c => c.Id == appointment.ClientId)
            .Select(c => new { c.FirstName, c.Phone, c.Email })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var serviceName = await db.Services.AsNoTracking().Where(s => s.Id == appointment.ServiceId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var staffName = await db.StaffMembers.AsNoTracking().Where(s => s.Id == appointment.StaffMemberId).Select(s => s.DisplayName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;

        // One visit of several services (ADR 0095) is spoken of once: its first active part names them all, and the
        // later parts say so, so that a confirmation or a reminder is not sent for each.
        var laterPart = false;
        if (appointment.VisitGroupId is { } groupId && AppointmentStateMachine.IsActive(appointment.Status))
        {
            var active = AppointmentStateMachine.Active.ToArray();
            var parts = await db.Appointments.AsNoTracking()
                .Where(a => a.VisitGroupId == groupId && active.Contains(a.Status))
                .OrderBy(a => a.Start)
                .Select(a => new { a.Id, a.ServiceId })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (parts.Count > 1)
            {
                laterPart = parts[0].Id != appointment.Id;
                if (!laterPart)
                {
                    var partIds = parts.Select(p => p.ServiceId).ToList();
                    var names = await db.Services.AsNoTracking().Where(s => partIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken).ConfigureAwait(false);
                    serviceName = VisitRules.JoinNames(parts.Select(p => names.GetValueOrDefault(p.ServiceId, string.Empty)));
                }
            }
        }

        return new AppointmentMessageData(
            tenant,
            DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc,
            CultureInfo.GetCultureInfo(tenant.Culture),
            appointment.Id,
            appointment.Status,
            appointment.Start,
            appointment.PublicToken,
            client?.FirstName ?? string.Empty,
            string.IsNullOrWhiteSpace(client?.Phone) ? null : client.Phone,
            string.IsNullOrWhiteSpace(client?.Email) ? null : client.Email,
            serviceName,
            staffName)
        {
            IsLaterPartOfVisit = laterPart,
        };
    }
}

/// <param name="PublicToken">The secret of the client's own link; null only for a desk booking made before ADR 0029.</param>
/// <param name="ClientPhone">Null when the card has no phone number: there is nobody to text.</param>
/// <param name="ClientEmail">Null when the card has no email address.</param>
public sealed record AppointmentMessageData(
    TenantInfo Tenant,
    DateTimeZone Zone,
    CultureInfo Culture,
    Guid AppointmentId,
    AppointmentStatus Status,
    Instant Start,
    string? PublicToken,
    string ClientFirstName,
    string? ClientPhone,
    string? ClientEmail,
    string ServiceName,
    string StaffName)
{
    public bool IsActive => AppointmentStateMachine.IsActive(Status);

    /// <summary>
    /// A later part of a visit of several services (ADR 0095): the first part's message names every service, so none
    /// is sent for this one when the visit is booked or a reminder is due.
    /// </summary>
    public bool IsLaterPartOfVisit { get; init; }
}