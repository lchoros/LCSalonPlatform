using Microsoft.Extensions.Logging;

using NodaTime;

using Salon.Core.Messaging;

namespace Salon.Services.Notifications;

/// <summary>The SMS channel of <see cref="AppointmentMovedNotice"/>; a consumer of its own so the outbox retries it alone (ADR 0025).</summary>
public sealed class AppointmentMovedSmsConsumer(
    AppointmentMessageReader reader,
    IPublicUrlBuilder urls,
    AppointmentNotifier notifier,
    IClock clock,
    ILogger<AppointmentMovedSmsConsumer> logger)
    : AppointmentMovedNotice(MessageChannel.Sms, reader, urls, notifier, clock, logger);