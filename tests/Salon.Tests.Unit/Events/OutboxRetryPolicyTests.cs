using NodaTime;
using Salon.Core.Events;
using Salon.Services.Events;

namespace Salon.Tests.Unit.Events;

/// <summary>Exponential backoff (30 s × 2^(attempts−1), capped at one hour) and dead-lettering at the attempt limit.</summary>
public class OutboxRetryPolicyTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 11, 12, 0);

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(5, 480)]
    [InlineData(7, 1920)]
    [InlineData(8, 3600)]
    [InlineData(40, 3600)]
    public void Backoff_doubles_from_thirty_seconds_and_caps_at_one_hour(int attempts, int expectedSeconds)
    {
        OutboxRetryPolicy.Backoff(attempts).ShouldBe(Duration.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void First_failure_schedules_a_retry_and_keeps_the_message_alive()
    {
        var message = NewMessage();

        OutboxRetryPolicy.RecordFailure(message, new InvalidOperationException("consumer blew up"), Now, maxAttempts: 5);

        message.Attempts.ShouldBe(1);
        message.LastError.ShouldBe("InvalidOperationException: consumer blew up");
        message.NextAttemptAt.ShouldBe(Now + Duration.FromSeconds(30));
        message.FailedAt.ShouldBeNull();
        message.ProcessedAt.ShouldBeNull();
    }

    [Fact]
    public void Reaching_the_attempt_limit_dead_letters_the_message()
    {
        var message = NewMessage();
        message.Attempts = 4;

        OutboxRetryPolicy.RecordFailure(message, new InvalidOperationException("still broken"), Now, maxAttempts: 5);

        message.Attempts.ShouldBe(5);
        message.FailedAt.ShouldBe(Now);
        message.NextAttemptAt.ShouldBeNull();
        message.LastError.ShouldBe("InvalidOperationException: still broken");
    }

    [Fact]
    public void The_stored_error_names_the_innermost_cause()
    {
        var error = new InvalidOperationException("consumer failed", new TimeoutException("outer", new IOException("disk full")));

        OutboxRetryPolicy.Describe(error).ShouldBe("InvalidOperationException: consumer failed <- IOException: disk full");
    }

    [Fact]
    public void The_stored_error_has_no_nul_characters_because_postgres_text_refuses_them()
    {
        OutboxRetryPolicy.Describe(new InvalidOperationException("boom\0bang\0")).ShouldBe("InvalidOperationException: boombang");
    }

    [Fact]
    public void The_stored_error_is_cut_to_the_column_length()
    {
        var text = OutboxRetryPolicy.Describe(new InvalidOperationException(new string('x', 5000)));

        text.Length.ShouldBe(OutboxMessage.MaxErrorLength);
        OutboxMessage.MaxErrorLength.ShouldBe(4000);
    }

    private static OutboxMessage NewMessage() => new()
    {
        EventType = "Salon.Tests.Unit.Events.ThingHappened",
        Payload = "{}",
        OccurredAt = Now - Duration.FromMinutes(1),
    };
}