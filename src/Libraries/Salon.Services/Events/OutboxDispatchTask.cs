using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Salon.Core.Tasks;

namespace Salon.Services.Events;

/// <summary>Every minute, delivers pending outbox rows batch by batch (ADR 0007). Hangfire keeps one instance running at a time (ADR 0005).</summary>
internal sealed class OutboxDispatchTask(IOutboxDispatcher dispatcher, IOptions<OutboxOptions> options, ILogger<OutboxDispatchTask> logger) : IScheduleTask
{
    public const string Name = "Events.DispatchOutbox";

    /// <summary>Upper bound on passes per occurrence (5000 rows at the default batch size); the next tick carries on.</summary>
    public const int MaxPassesPerRun = 50;

    public string SystemName => Name;

    public string CronExpression => "* * * * *";

    /// <summary>
    /// Keeps dispatching while every pass comes back full. Only a pass that handles fewer rows than the batch size,
    /// i.e. an inflow smaller than one batch during that pass, ends the occurrence early; a sustained flood is cut off
    /// by <see cref="MaxPassesPerRun"/> so one occurrence cannot run forever.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var batchSize = options.Value.BatchSize;
        for (var pass = 1; pass <= MaxPassesPerRun; pass++)
        {
            var processed = await dispatcher.DispatchPendingAsync(batchSize, cancellationToken).ConfigureAwait(false);
            if (processed < batchSize)
            {
                return;
            }
        }

        logger.LogWarning("Outbox dispatch stopped after {Passes} full batches of {BatchSize}; the next occurrence continues", MaxPassesPerRun, batchSize);
    }
}