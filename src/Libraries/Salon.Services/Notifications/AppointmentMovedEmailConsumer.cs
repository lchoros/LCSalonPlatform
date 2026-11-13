using Microsoft.Extensions.Logging;

using NodaTime;

using Salon.Core.Messaging;

namespace Salon.Services.Notifications;

/// <summary>The email channel of <see cref="AppointmentMovedNotice"/>; a consumer of its own so the outbox retries it alone (ADR 0025).</summary>
public sealed class AppointmentMovedEmailConsumer(
    AppointmentMessageReader reader,
    IPublicUrlBuilder urls,
    AppointmentNotifier notifier,
    IClock clock,
    ILogger<AppointmentMovedEmailConsumer> logger)
    : AppointmentMovedNotice(MessageChannel.Email, reader, urls, notifier, clock, logger);