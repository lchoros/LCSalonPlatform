using Microsoft.Extensions.Logging;

using NodaTime;

using Salon.Core.Appointments.Events;
using Salon.Core.Events;
using Salon.Core.Messaging;

namespace Salon.Services.Notifications;

/// <summary>
/// Tells the client the new time of a visit that was moved (ADR 0021), by themselves through the link or by the salon
/// in the calendar. One rule, two channels (<see cref="AppointmentMovedSmsConsumer"/>,
/// <see cref="AppointmentMovedEmailConsumer"/>), each retried on its own (ADR 0025). Skipped when the visit has been
/// moved again since (the later event carries the news), is no longer active or is already in the past.
/// </summary>
public abstract class AppointmentMovedNotice : IConsumer<AppointmentMoved>
{
    private readonly MessageChannel _channel;
    private readonly AppointmentMessageReader _reader;
    private readonly IPublicUrlBuilder _urls;
    private readonly AppointmentNotifier _notifier;
    private readonly IClock _clock;
    private readonly ILogger _logger;

    protected AppointmentMovedNotice(MessageChannel channel, AppointmentMessageReader reader, IPublicUrlBuilder urls, AppointmentNotifier notifier, IClock clock, ILogger logger)
    {
        _channel = channel;
        _reader = reader;
        _urls = urls;
        _notifier = notifier;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleAsync(AppointmentMoved domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var data = await _reader.ReadAsync(domainEvent.AppointmentId, cancellationToken).ConfigureAwait(false);
        if (data is null || !data.IsActive)
        {
            _logger.LogInformation("Appointment {AppointmentId} is gone or no longer active; moved message skipped", domainEvent.AppointmentId);
            return;
        }

        if (data.Start != domainEvent.NewStart)
        {
            _logger.LogDebug("Appointment {AppointmentId} has been moved again since this event; the later one sends the message", data.AppointmentId);
            return;
        }

        if (data.Start <= _clock.GetCurrentInstant())
        {
            _logger.LogDebug("Appointment {AppointmentId} was moved into the past; no message", data.AppointmentId);
            return;
        }

        // The client's own link when the booking has one; a booking made at the desk gets the salon's booking page (ADR 0014).
        var link = data.PublicToken is { } token ? _urls.CancelBookingUrl(data.Tenant.Slug, token) : _urls.BookingUrl(data.Tenant.Slug);
        var tokens = BookingSmsTokens.Confirmation(data.Tenant.Name, data.ClientFirstName, data.ServiceName, data.StaffName, data.Start, data.Zone, data.Culture, link);
        await _notifier.SendAsync(_channel, MessageTemplateCatalog.BookingRescheduled, data, tokens, cancellationToken).ConfigureAwait(false);
    }
}