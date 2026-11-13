using Microsoft.Extensions.Logging;

using Salon.Core.Appointments.Events;
using Salon.Core.Events;
using Salon.Core.Messaging;
using Salon.Services.Messaging;

namespace Salon.Services.Notifications;

/// <summary>
/// Tells the client by SMS that their visit is off (ADR 0021), whoever cancelled it: a client who used the link gets the
/// confirmation in writing, a client whose visit the salon cancelled learns about it at all. Nothing is sent for a visit
/// that had already started (tidying up the diary afterwards) or to a card without a phone number.
/// </summary>
public sealed class AppointmentCancelledSmsConsumer(
    AppointmentMessageReader reader,
    IMessageTemplateService templates,
    IPublicUrlBuilder urls,
    ISmsSender sms,
    ILogger<AppointmentCancelledSmsConsumer> logger) : IConsumer<AppointmentCancelled>
{
    public async Task HandleAsync(AppointmentCancelled domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var data = await reader.ReadAsync(domainEvent.AppointmentId, cancellationToken).ConfigureAwait(false);
        if (data is null || data.IsActive)
        {
            logger.LogInformation("Appointment {AppointmentId} is gone or active again; cancellation SMS skipped", domainEvent.AppointmentId);
            return;
        }

        if (data.Start <= domainEvent.At)
        {
            logger.LogDebug("Appointment {AppointmentId} was cancelled after it started; no SMS", data.AppointmentId);
            return;
        }

        if (data.ClientPhone is null)
        {
            logger.LogInformation("The client of appointment {AppointmentId} has no phone; cancellation SMS skipped", data.AppointmentId);
            return;
        }

        var tokens = BookingSmsTokens.Cancelled(
            data.Tenant.Name,
            data.ClientFirstName,
            data.ServiceName,
            data.StaffName,
            data.Start,
            data.Zone,
            data.Culture,
            urls.BookingUrl(data.Tenant.Slug));
        var rendered = await templates.RenderAsync(MessageTemplateCatalog.BookingCancelled, MessageChannel.Sms, data.Tenant.Culture, tokens, cancellationToken).ConfigureAwait(false);
        if (!rendered.IsComplete)
        {
            logger.LogWarning("Cancellation template for tenant {TenantId} has unresolved tokens: {Tokens}", data.Tenant.Id, string.Join(", ", rendered.MissingTokens));
        }

        await sms.SendAsync(new SmsMessage(data.ClientPhone, rendered.Body), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Cancellation SMS for appointment {AppointmentId} handed to {Provider}", data.AppointmentId, sms.SystemName);
    }
}