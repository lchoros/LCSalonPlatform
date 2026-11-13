using Microsoft.Extensions.Logging;

using Salon.Core.Appointments;
using Salon.Core.Appointments.Events;
using Salon.Core.Events;
using Salon.Core.Messaging;

namespace Salon.Services.Notifications;

/// <summary>
/// The booking confirmation (ADR 0014) for an <em>online</em> booking: template
/// <see cref="MessageTemplateCatalog.BookingConfirmation"/> in the salon's culture with the cancel link built from the
/// appointment's public token. One rule, two channels: <see cref="AppointmentBookedSmsConsumer"/> and
/// <see cref="AppointmentBookedEmailConsumer"/> each run it for their own channel, so the outbox can retry one without
/// repeating the other (ADR 0025). Runs in the publishing tenant's scope (ADR 0007). Skipped, with a log line carrying
/// ids only, when the appointment was made in the admin, is no longer active or has no public token.
/// </summary>
public abstract class AppointmentBookedNotice : IConsumer<AppointmentBooked>
{
    private readonly MessageChannel _channel;
    private readonly AppointmentMessageReader _reader;
    private readonly IPublicUrlBuilder _urls;
    private readonly AppointmentNotifier _notifier;
    private readonly ILogger _logger;

    protected AppointmentBookedNotice(MessageChannel channel, AppointmentMessageReader reader, IPublicUrlBuilder urls, AppointmentNotifier notifier, ILogger logger)
    {
        _channel = channel;
        _reader = reader;
        _urls = urls;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task HandleAsync(AppointmentBooked domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (domainEvent.Source != AppointmentSource.Online)
        {
            _logger.LogDebug("Appointment {AppointmentId} was booked in the admin; no confirmation is sent", domainEvent.AppointmentId);
            return;
        }

        var data = await _reader.ReadAsync(domainEvent.AppointmentId, cancellationToken).ConfigureAwait(false);
        if (data is null || !data.IsActive)
        {
            _logger.LogInformation("Appointment {AppointmentId} is gone or no longer active; confirmation skipped", domainEvent.AppointmentId);
            return;
        }

        if (data.PublicToken is null)
        {
            _logger.LogWarning("Online appointment {AppointmentId} has no public token; confirmation skipped", data.AppointmentId);
            return;
        }

        var tokens = BookingSmsTokens.Confirmation(
            data.Tenant.Name,
            data.ClientFirstName,
            data.ServiceName,
            data.StaffName,
            data.Start,
            data.Zone,
            data.Culture,
            _urls.CancelBookingUrl(data.Tenant.Slug, data.PublicToken));
        await _notifier.SendAsync(_channel, MessageTemplateCatalog.BookingConfirmation, data, tokens, cancellationToken).ConfigureAwait(false);
    }
}