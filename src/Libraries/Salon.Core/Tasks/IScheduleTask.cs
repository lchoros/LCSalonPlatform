namespace Salon.Core.Tasks;

/// <summary>
/// A recurring background task (reminders, outbox dispatch, clean-ups). Tasks run platform-wide with no tenant;
/// use <c>ITenantScopeRunner</c> to do per-tenant work. Hangfire triggers them by <see cref="SystemName"/> under a
/// distributed lock, so only one instance runs at a time even with several servers.
/// </summary>
public interface IScheduleTask
{
    /// <summary>Stable identifier, e.g. "Notifications.SendReminders". Doubles as the Hangfire recurring job id.</summary>
    string SystemName { get; }

    /// <summary>Default cron expression (5 fields, UTC). A per-task setting can override it later.</summary>
    string CronExpression { get; }

    Task ExecuteAsync(CancellationToken cancellationToken);
}