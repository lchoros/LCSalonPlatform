using Microsoft.Extensions.Logging;

using Salon.Core.Messaging;

namespace Salon.Services.Notifications;

/// <summary>The email channel of <see cref="AppointmentCancelledNotice"/>; a consumer of its own so the outbox retries it alone (ADR 0025).</summary>
public sealed class AppointmentCancelledEmailConsumer(
    AppointmentMessageReader reader,
    IPublicUrlBuilder urls,
    AppointmentNotifier notifier,
    ILogger<AppointmentCancelledEmailConsumer> logger)
    : AppointmentCancelledNotice(MessageChannel.Email, reader, urls, notifier, logger);