using NodaTime;

using Salon.Core.Domain;

namespace Salon.Core.Events;

/// <summary>
/// One published domain event waiting for, or finished with, dispatch (ADR 0007). Platform-level on purpose: the
/// dispatcher reads the rows of every tenant in one query, so this table has no tenant filter and no RLS policy.
/// Consumers still run inside the <see cref="TenantId"/> scope. Only Salon.Services.Events and the Data mapping may
/// reference this type (architecture test OutboxIsolationTests).
/// </summary>
public class OutboxMessage : BaseEntity
{
    /// <summary>Column length of <see cref="LastError"/>.</summary>
    public const int MaxErrorLength = 4000;

    /// <summary>The publishing tenant; null for platform events.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Stable registry name of the event type (its CLR full name).</summary>
    public required string EventType { get; set; }

    /// <summary>The event serialised as JSON.</summary>
    public required string Payload { get; set; }

    public Instant OccurredAt { get; set; }

    /// <summary>Set once every consumer has succeeded (or none exists).</summary>
    public Instant? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    /// <summary>Dead letter: the attempt limit was reached; never retried automatically.</summary>
    public Instant? FailedAt { get; set; }

    /// <summary>Earliest time for the next attempt; null means as soon as possible.</summary>
    public Instant? NextAttemptAt { get; set; }
}