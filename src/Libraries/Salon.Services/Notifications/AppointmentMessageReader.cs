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
/// culture, the client's first name and phone, the service and the person. Used by the SMS consumers so that each of
/// them is only its own rule (ADR 0021).
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
            .Select(a => new { a.Id, a.ClientId, a.ServiceId, a.StaffMemberId, a.Start, a.Status, a.PublicToken })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (appointment is null)
        {
            return null;
        }

        var client = await db.Clients.AsNoTracking()
            .Where(c => c.Id == appointment.ClientId)
            .Select(c => new { c.FirstName, c.Phone })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var serviceName = await db.Services.AsNoTracking().Where(s => s.Id == appointment.ServiceId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var staffName = await db.StaffMembers.AsNoTracking().Where(s => s.Id == appointment.StaffMemberId).Select(s => s.DisplayName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;

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
            serviceName,
            staffName);
    }
}

/// <param name="PublicToken">The secret of the client's own link; null for a booking made at the desk (ADR 0014).</param>
/// <param name="ClientPhone">Null when the card has no phone number: there is nobody to text.</param>
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
    string ServiceName,
    string StaffName)
{
    public bool IsActive => AppointmentStateMachine.IsActive(Status);
}