using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Tasks;
using Salon.Services.Tenancy;

namespace Salon.Services.Notifications;

/// <summary>Every five minutes, salon by salon: send the reminders that have become due (ADR 0020).</summary>
internal sealed class AppointmentReminderTask(ITenantScopeRunner tenants, IClock clock, ILogger<AppointmentReminderTask> logger) : IScheduleTask
{
    public const string Name = "Notifications.SendReminders";

    public string SystemName => Name;

    public string CronExpression => "*/5 * * * *";

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetCurrentInstant();
        return tenants.ForEachTenantAsync(async (services, tenant, ct) =>
        {
            var sent = await services.GetRequiredService<IAppointmentReminderService>().SendDueAsync(now, ct).ConfigureAwait(false);
            if (sent > 0)
            {
                logger.LogInformation("Sent {Count} appointment reminders for tenant {Slug}", sent, tenant.Slug);
            }
        }, cancellationToken);
    }
}