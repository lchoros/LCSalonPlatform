using Microsoft.Extensions.Logging;
using Salon.Core.Appointments;
using Salon.Core.Appointments.Events;
using Salon.Core.Events;
using Salon.Core.Messaging;
using Salon.Core.Settings;
using Salon.Services.Settings;

namespace Salon.Services.Notifications;

/// <summary>
/// The booking confirmation (ADR 0014, ADR 0029): template <see cref="MessageTemplateCatalog.BookingConfirmation"/> in
/// the salon's culture with the client's own link built from the appointment's public token. An online booking is
/// always confirmed; a booking the salon made at the desk is confirmed unless the salon switched that off
/// (<see cref="NotificationSettings.ConfirmDeskBookings"/>). One rule, two channels:
/// <see cref="AppointmentBookedSmsConsumer"/> and <see cref="AppointmentBookedEmailConsumer"/> each run it for their
/// own channel, so the outbox can retry one without repeating the other (ADR 0025). Runs in the publishing tenant's
/// scope (ADR 0007). Skipped, with a log line carrying ids only, when the appointment is gone or no longer active.
/// </summary>
public abstract class AppointmentBookedNotice : IConsumer<AppointmentBooked>
{
    private readonly MessageChannel _channel;
    private readonly AppointmentMessageReader _reader;
    private readonly IPublicUrlBuilder _urls;
    private readonly AppointmentNotifier _notifier;
    private readonly ISettingService _settings;
    private readonly ILogger _logger;

    protected AppointmentBookedNotice(
        MessageChannel channel,
        AppointmentMessageReader reader,
        IPublicUrlBuilder urls,
        AppointmentNotifier notifier,
        ISettingService settings,
        ILogger logger)
    {
        _channel = channel;
        _reader = reader;
        _urls = urls;
        _notifier = notifier;
        _settings = settings;
        _logger = logger;
    }

    public async Task HandleAsync(AppointmentBooked domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (domainEvent.Source != AppointmentSource.Online
            && !(await _settings.LoadAsync<NotificationSettings>(cancellationToken).ConfigureAwait(false)).ConfirmDeskBookings)
        {
            _logger.LogDebug("Appointment {AppointmentId} was booked at the desk and the salon does not confirm those", domainEvent.AppointmentId);
            return;
        }

        var data = await _reader.ReadAsync(domainEvent.AppointmentId, cancellationToken).ConfigureAwait(false);
        if (data is null || !data.IsActive)
        {
            _logger.LogInformation("Appointment {AppointmentId} is gone or no longer active; confirmation skipped", domainEvent.AppointmentId);
            return;
        }

        // Every booking has a token since ADR 0029; a row from before it leads to the salon's booking page instead.
        var link = data.PublicToken is { } token ? _urls.CancelBookingUrl(data.Tenant.Slug, token) : _urls.BookingUrl(data.Tenant.Slug);
        var tokens = BookingSmsTokens.Confirmation(
            data.Tenant.Name,
            data.ClientFirstName,
            data.ServiceName,
            data.StaffName,
            data.Start,
            data.Zone,
            data.Culture,
            link);
        await _notifier.SendAsync(_channel, MessageTemplateCatalog.BookingConfirmation, data, tokens, cancellationToken).ConfigureAwait(false);
    }
}