using Microsoft.Extensions.Logging;
using Salon.Core.Messaging;
using Salon.Services.Settings;

namespace Salon.Services.Notifications;

/// <summary>The SMS channel of <see cref="AppointmentBookedNotice"/>; a consumer of its own so the outbox retries it alone (ADR 0025).</summary>
public sealed class AppointmentBookedSmsConsumer(
    AppointmentMessageReader reader,
    IPublicUrlBuilder urls,
    AppointmentNotifier notifier,
    ISettingService settings,
    ILogger<AppointmentBookedSmsConsumer> logger)
    : AppointmentBookedNotice(MessageChannel.Sms, reader, urls, notifier, settings, logger);