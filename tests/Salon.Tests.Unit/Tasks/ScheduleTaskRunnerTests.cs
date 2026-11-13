using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Salon.Core.Tasks;
using Salon.Services.Tasks;

namespace Salon.Tests.Unit.Tasks;

public class ScheduleTaskRunnerTests
{
    private static IScheduleTask Task_(string name)
    {
        var task = Substitute.For<IScheduleTask>();
        task.SystemName.Returns(name);
        task.CronExpression.Returns("* * * * *");
        return task;
    }

    [Fact]
    public async Task Runs_the_task_with_the_given_system_name_only()
    {
        var a = Task_("A");
        var b = Task_("B");
        var runner = new ScheduleTaskRunner([a, b], NullLogger<ScheduleTaskRunner>.Instance);

        await runner.RunAsync("B", CancellationToken.None);

        await b.Received(1).ExecuteAsync(Arg.Any<CancellationToken>());
        await a.DidNotReceive().ExecuteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_system_name_fails_loudly()
    {
        var runner = new ScheduleTaskRunner([Task_("A")], NullLogger<ScheduleTaskRunner>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(() => runner.RunAsync("Nope", CancellationToken.None));
    }

    [Fact]
    public async Task Task_failures_propagate_so_hangfire_records_them()
    {
        var failing = Task_("Boom");
        failing.ExecuteAsync(Arg.Any<CancellationToken>()).Returns(_ => throw new InvalidOperationException("boom"));
        var runner = new ScheduleTaskRunner([failing], NullLogger<ScheduleTaskRunner>.Instance);

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => runner.RunAsync("Boom", CancellationToken.None));
        ex.Message.ShouldBe("boom");
    }

    [Fact]
    public void Runner_entry_point_is_guarded_by_a_distributed_lock_and_does_not_retry()
    {
        var method = typeof(ScheduleTaskRunner).GetMethod(nameof(ScheduleTaskRunner.RunAsync))!;

        var lockAttribute = method.GetCustomAttributes(typeof(Hangfire.DisableConcurrentExecutionAttribute), false)
            .Cast<Hangfire.DisableConcurrentExecutionAttribute>().ShouldHaveSingleItem();
        // Without an explicit resource Hangfire would use the runner method itself as the lock, shared by every task.
        lockAttribute.Resource.ShouldBe(ScheduleTaskRunner.LockResource);
        ScheduleTaskRunner.LockResource.ShouldContain("{0}");
        var retry = method.GetCustomAttributes(typeof(Hangfire.AutomaticRetryAttribute), false).Cast<Hangfire.AutomaticRetryAttribute>().Single();
        retry.Attempts.ShouldBe(0);
    }
}