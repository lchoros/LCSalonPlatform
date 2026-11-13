using System.Diagnostics;
using Hangfire;
using Microsoft.Extensions.Logging;
using Salon.Core.Tasks;

namespace Salon.Services.Tasks;

/// <summary>
/// The single Hangfire entry point for schedule tasks. One recurring job per <see cref="IScheduleTask"/> calls
/// <see cref="RunAsync"/> with the task's SystemName. The distributed lock lives in Hangfire's Postgres storage.
/// </summary>
public sealed class ScheduleTaskRunner(IEnumerable<IScheduleTask> tasks, ILogger<ScheduleTaskRunner> logger)
{
    /// <summary>Wait at most this long for a running instance of the same task before giving up this occurrence.</summary>
    public const int LockTimeoutSeconds = 30;

    /// <summary>One lock per task: Hangfire formats {0} with the first job argument (the SystemName). Without a resource every task would share one global lock.</summary>
    public const string LockResource = "Salon.ScheduleTask.{0}";

    [DisableConcurrentExecution(LockResource, LockTimeoutSeconds)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(string systemName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
        var task = tasks.FirstOrDefault(t => string.Equals(t.SystemName, systemName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No IScheduleTask named '{systemName}' is registered.");

        var watch = Stopwatch.StartNew();
        logger.LogInformation("Schedule task {Task} starting", systemName);
        try
        {
            await task.ExecuteAsync(cancellationToken);
            logger.LogInformation("Schedule task {Task} finished in {Elapsed} ms", systemName, watch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Schedule task {Task} failed after {Elapsed} ms", systemName, watch.ElapsedMilliseconds);
            throw;
        }
    }
}