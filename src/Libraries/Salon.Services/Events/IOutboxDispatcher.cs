namespace Salon.Services.Events;

/// <summary>Delivers pending outbox rows to their consumers (ADR 0007). <see cref="OutboxDispatchTask"/> drives it; tests call it directly.</summary>
/// <remarks>
/// Not safe to run concurrently: two callers loading the same pending rows both run the consumers (delivery is
/// at-least-once anyway); the row's xmin token then lets only the first outcome be saved. Today only Hangfire's per-task
/// lock (ADR 0005) serialises the scheduled path; do not call this from request code.
/// </remarks>
public interface IOutboxDispatcher
{
    /// <summary>
    /// Handles up to <paramref name="batchSize"/> due messages (default <c>Outbox:BatchSize</c>), approximately oldest
    /// first, and returns how many were handled, whether they succeeded or failed.
    /// </summary>
    Task<int> DispatchPendingAsync(int? batchSize = null, CancellationToken cancellationToken = default);
}