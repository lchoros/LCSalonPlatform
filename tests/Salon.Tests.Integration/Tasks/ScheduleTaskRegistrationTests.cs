using Hangfire;
using Hangfire.Storage;

using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Tasks;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Tasks;

[Collection(SharedDatabase.Name)]
public class ScheduleTaskRegistrationTests(SalonWebApplicationFactory factory)
{
    [Fact]
    public void Every_schedule_task_has_a_recurring_hangfire_job_with_its_cron()
    {
        using var scope = factory.Services.CreateScope();
        var tasks = scope.ServiceProvider.GetServices<IScheduleTask>().ToList();
        tasks.ShouldNotBeEmpty();

        using var connection = factory.Services.GetRequiredService<JobStorage>().GetConnection();
        var jobs = connection.GetRecurringJobs();

        foreach (var task in tasks)
        {
            var job = jobs.SingleOrDefault(j => j.Id == task.SystemName);
            job.ShouldNotBeNull($"no recurring job for {task.SystemName}");
            job.Cron.ShouldBe(task.CronExpression);
        }
    }
}