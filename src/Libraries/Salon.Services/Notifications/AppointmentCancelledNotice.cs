using Microsoft.Extensions.Logging;

using Salon.Core.Appointments.Events;
using Salon.Core.Events;
using Salon.Core.Messaging;

namespace Salon.Services.Notifications;

/// <summary>
/// Tells the client that their visit is off (ADR 0021), whoever cancelled it: a client who used the link gets the
/// confirmation in writing, a client whose visit the salon cancelled learns about it at all. One rule, two channels
/// (<see cref="AppointmentCancelledSmsConsumer"/>, <see cref="AppointmentCancelledEmailConsumer"/>), each retried on
/// its own (ADR 0025). Nothing is sent for a visit that had already started (tidying up the diary afterwards).
/// </summary>
public abstract class AppointmentCancelledNotice : IConsumer<AppointmentCancelled>
{
    private readonly MessageChannel _channel;
    private readonly AppointmentMessageReader _reader;
    private readonly IPublicUrlBuilder _urls;
    private readonly AppointmentNotifier _notifier;
    private readonly ILogger _logger;

    protected AppointmentCancelledNotice(MessageChannel channel, AppointmentMessageReader reader, IPublicUrlBuilder urls, AppointmentNotifier notifier, ILogger logger)
    {
        _channel = channel;
        _reader = reader;
        _urls = urls;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task HandleAsync(AppointmentCancelled domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var data = await _reader.ReadAsync(domainEvent.AppointmentId, cancellationToken).ConfigureAwait(false);
        if (data is null || data.IsActive)
        {
            _logger.LogInformation("Appointment {AppointmentId} is gone or active again; cancellation message skipped", domainEvent.AppointmentId);
            return;
        }

        if (data.Start <= domainEvent.At)
        {
            _logger.LogDebug("Appointment {AppointmentId} was cancelled after it started; no message", data.AppointmentId);
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
            _urls.BookingUrl(data.Tenant.Slug));
        await _notifier.SendAsync(_channel, MessageTemplateCatalog.BookingCancelled, data, tokens, cancellationToken).ConfigureAwait(false);
    }
}