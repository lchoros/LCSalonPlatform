using NodaTime;
using Salon.Core.Events;

namespace Salon.Services.Events;

/// <summary>Retry state transitions for a failed outbox message: exponential backoff, then dead letter at the attempt limit.</summary>
internal static class OutboxRetryPolicy
{
    private static readonly Duration BaseDelay = Duration.FromSeconds(30);
    private static readonly Duration MaxDelay = Duration.FromHours(1);

    /// <summary>30 s × 2^(attempts − 1), capped at one hour: 30 s, 1 min, 2 min, 4 min, 8 min, ...</summary>
    public static Duration Backoff(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var delay = BaseDelay * (1L << Math.Min(attempts - 1, 20));
        return Duration.Min(delay, MaxDelay);
    }

    public static void RecordFailure(OutboxMessage message, Exception error, Instant now, int maxAttempts)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(error);

        message.Attempts++;
        message.LastError = Describe(error);

        if (message.Attempts >= maxAttempts)
        {
            message.FailedAt = now;
            message.NextAttemptAt = null;
        }
        else
        {
            message.NextAttemptAt = now + Backoff(message.Attempts);
        }
    }

    /// <summary>
    /// "Type: message", followed by the innermost cause when there is one. NUL characters are removed because Postgres
    /// text columns refuse them (a consumer's message must never make the outcome unsaveable), then cut to the column length.
    /// </summary>
    public static string Describe(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var text = $"{error.GetType().Name}: {error.Message}";
        var innermost = error;
        while (innermost.InnerException is not null)
        {
            innermost = innermost.InnerException;
        }

        if (!ReferenceEquals(innermost, error))
        {
            text += $" <- {innermost.GetType().Name}: {innermost.Message}";
        }

        text = text.Replace("\0", string.Empty, StringComparison.Ordinal);
        return text.Length > OutboxMessage.MaxErrorLength ? text[..OutboxMessage.MaxErrorLength] : text;
    }
}