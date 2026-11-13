using Microsoft.Extensions.Logging;

using Salon.Core.Messaging;

namespace Salon.Services.Notifications;

/// <summary>The SMS channel of <see cref="AppointmentBookedNotice"/>; a consumer of its own so the outbox retries it alone (ADR 0025).</summary>
public sealed class AppointmentBookedSmsConsumer(
    AppointmentMessageReader reader,
    IPublicUrlBuilder urls,
    AppointmentNotifier notifier,
    ILogger<AppointmentBookedSmsConsumer> logger)
    : AppointmentBookedNotice(MessageChannel.Sms, reader, urls, notifier, logger);