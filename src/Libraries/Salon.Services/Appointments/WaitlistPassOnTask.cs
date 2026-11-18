using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Tasks;
using Salon.Services.Tenancy;

namespace Salon.Services.Appointments;

/// <summary>
/// Every minute, salon by salon: a freed time whose hold ran out, or whose client stopped waiting, goes to the next
/// client in line (ADR 0065). The hold itself ends by the clock on the booking pages; this only passes the time on,
/// so a run that is a minute late keeps nobody from booking.
/// </summary>
internal sealed class WaitlistPassOnTask(ITenantScopeRunner tenants, IClock clock, ILogger<WaitlistPassOnTask> logger) : IScheduleTask
{
    public const string Name = "Waitlist.PassOnHeldTimes";

    public string SystemName => Name;

    public string CronExpression => "* * * * *";

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetCurrentInstant();
        return tenants.ForEachTenantAsync(async (services, tenant, ct) =>
        {
            var told = await services.GetRequiredService<IWaitlistOfferService>().PassOnDueAsync(now, ct).ConfigureAwait(false);
            if (told > 0)
            {
                logger.LogInformation("Passed {Count} freed times on to the next waiting client for tenant {Slug}", told, tenant.Slug);
            }
        }, cancellationToken);
    }
}