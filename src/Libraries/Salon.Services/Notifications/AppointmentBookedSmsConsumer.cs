using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Appointments.Events;
using Salon.Core.Events;
using Salon.Core.Messaging;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Messaging;
using Salon.Services.Tenancy;

namespace Salon.Services.Notifications;

/// <summary>
/// Sends the booking confirmation SMS (ADR 0014) when an <em>online</em> booking is made: template
/// <see cref="MessageTemplateCatalog.BookingConfirmation"/> in the salon's culture with the cancel link built from the
/// appointment's public token. Runs from the outbox in the publishing tenant's scope (ADR 0007), so every lookup goes
/// through the tenant filter. Skipped, with a log line carrying ids only, when the client has no phone, the appointment
/// was made in the admin (those notifications are M2 work), or the appointment is no longer active.
/// </summary>
public sealed class AppointmentBookedSmsConsumer(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IMessageTemplateService templates,
    IPublicUrlBuilder urls,
    ISmsSender sms,
    ILogger<AppointmentBookedSmsConsumer> logger) : IConsumer<AppointmentBooked>
{
    public async Task HandleAsync(AppointmentBooked domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (domainEvent.Source != AppointmentSource.Online)
        {
            logger.LogDebug("Appointment {AppointmentId} was booked in the admin; no confirmation SMS in M1", domainEvent.AppointmentId);
            return;
        }

        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("The confirmation SMS consumer needs the booking's tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");

        var appointment = await db.Appointments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == domainEvent.AppointmentId, cancellationToken).ConfigureAwait(false);
        if (appointment is null || !AppointmentStateMachine.IsActive(appointment.Status))
        {
            logger.LogInformation("Appointment {AppointmentId} is gone or no longer active; confirmation SMS skipped", domainEvent.AppointmentId);
            return;
        }

        if (appointment.PublicToken is null)
        {
            logger.LogWarning("Online appointment {AppointmentId} has no public token; confirmation SMS skipped", appointment.Id);
            return;
        }

        var client = await db.Clients.AsNoTracking()
            .Where(c => c.Id == appointment.ClientId)
            .Select(c => new { c.FirstName, c.Phone })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (client?.Phone is null)
        {
            logger.LogInformation("Client {ClientId} of appointment {AppointmentId} has no phone; confirmation SMS skipped", appointment.ClientId, appointment.Id);
            return;
        }

        var serviceName = await db.Services.AsNoTracking().Where(s => s.Id == appointment.ServiceId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var staffName = await db.StaffMembers.AsNoTracking().Where(s => s.Id == appointment.StaffMemberId).Select(s => s.DisplayName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;

        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        var culture = CultureInfo.GetCultureInfo(tenant.Culture);
        var tokens = BookingSmsTokens.Confirmation(
            tenant.Name,
            client.FirstName,
            serviceName,
            staffName,
            appointment.Start,
            zone,
            culture,
            urls.CancelBookingUrl(tenant.Slug, appointment.PublicToken));

        var rendered = await templates.RenderAsync(MessageTemplateCatalog.BookingConfirmation, MessageChannel.Sms, tenant.Culture, tokens, cancellationToken).ConfigureAwait(false);
        if (!rendered.IsComplete)
        {
            logger.LogWarning("Booking confirmation template for tenant {TenantId} has unresolved tokens: {Tokens}", tenantId, string.Join(", ", rendered.MissingTokens));
        }

        await sms.SendAsync(new SmsMessage(client.Phone, rendered.Body), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Confirmation SMS for appointment {AppointmentId} handed to {Provider}", appointment.Id, sms.SystemName);
    }
}