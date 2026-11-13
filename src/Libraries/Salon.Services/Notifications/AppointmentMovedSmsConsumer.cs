using Microsoft.Extensions.Logging;

using NodaTime;

using Salon.Core.Appointments.Events;
using Salon.Core.Events;
using Salon.Core.Messaging;
using Salon.Services.Messaging;

namespace Salon.Services.Notifications;

/// <summary>
/// Tells the client by SMS the new time of a visit that was moved (ADR 0021), by themselves through the link or by the
/// salon in the calendar. Skipped when the visit has been moved again since (the later event carries the news), is no
/// longer active, is already in the past, or the card has no phone number.
/// </summary>
public sealed class AppointmentMovedSmsConsumer(
    AppointmentMessageReader reader,
    IMessageTemplateService templates,
    IPublicUrlBuilder urls,
    ISmsSender sms,
    IClock clock,
    ILogger<AppointmentMovedSmsConsumer> logger) : IConsumer<AppointmentMoved>
{
    public async Task HandleAsync(AppointmentMoved domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var data = await reader.ReadAsync(domainEvent.AppointmentId, cancellationToken).ConfigureAwait(false);
        if (data is null || !data.IsActive)
        {
            logger.LogInformation("Appointment {AppointmentId} is gone or no longer active; moved SMS skipped", domainEvent.AppointmentId);
            return;
        }

        if (data.Start != domainEvent.NewStart)
        {
            logger.LogDebug("Appointment {AppointmentId} has been moved again since this event; the later one sends the SMS", data.AppointmentId);
            return;
        }

        if (data.Start <= clock.GetCurrentInstant())
        {
            logger.LogDebug("Appointment {AppointmentId} was moved into the past; no SMS", data.AppointmentId);
            return;
        }

        if (data.ClientPhone is null)
        {
            logger.LogInformation("The client of appointment {AppointmentId} has no phone; moved SMS skipped", data.AppointmentId);
            return;
        }

        // The client's own link when the booking has one; a booking made at the desk gets the salon's booking page (ADR 0014).
        var link = data.PublicToken is { } token ? urls.CancelBookingUrl(data.Tenant.Slug, token) : urls.BookingUrl(data.Tenant.Slug);
        var tokens = BookingSmsTokens.Confirmation(data.Tenant.Name, data.ClientFirstName, data.ServiceName, data.StaffName, data.Start, data.Zone, data.Culture, link);
        var rendered = await templates.RenderAsync(MessageTemplateCatalog.BookingRescheduled, MessageChannel.Sms, data.Tenant.Culture, tokens, cancellationToken).ConfigureAwait(false);
        if (!rendered.IsComplete)
        {
            logger.LogWarning("Rescheduled template for tenant {TenantId} has unresolved tokens: {Tokens}", data.Tenant.Id, string.Join(", ", rendered.MissingTokens));
        }

        await sms.SendAsync(new SmsMessage(data.ClientPhone, rendered.Body), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Moved SMS for appointment {AppointmentId} handed to {Provider}", data.AppointmentId, sms.SystemName);
    }
}