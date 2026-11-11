using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using Salon.Core.Tasks;
using Salon.Services.Events;

namespace Salon.Tests.Unit.Events;

/// <summary>The dispatch side of AddSalonEvents and the per-occurrence loop of the Hangfire task.</summary>
public class OutboxDispatchTaskTests
{
    [Fact]
    public void AddSalonEvents_registers_the_dispatcher_and_the_task_once()
    {
        var services = new ServiceCollection();

        services.AddSalonEvents();
        services.AddSalonEvents();

        services.Where(d => d.ServiceType == typeof(IOutboxDispatcher)).ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            d => d.ImplementationType.ShouldBe(typeof(OutboxDispatcher)),
            d => d.Lifetime.ShouldBe(ServiceLifetime.Scoped));
        services.Count(d => d.ServiceType == typeof(IScheduleTask) && d.ImplementationType == typeof(OutboxDispatchTask)).ShouldBe(1);
    }

    [Fact]
    public void The_task_resolves_from_DI_every_minute_under_a_stable_name()
    {
        var services = new ServiceCollection();
        services.AddSalonEvents();
        // BindConfiguration needs an IConfiguration at resolve time; the task only reads the defaults here.
        services.AddSingleton(Options.Create(new OutboxOptions()));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped(_ => Substitute.For<IOutboxDispatcher>());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var task = scope.ServiceProvider.GetServices<IScheduleTask>().OfType<OutboxDispatchTask>().Single();

        task.SystemName.ShouldBe("Events.DispatchOutbox");
        task.CronExpression.ShouldBe("* * * * *");
    }

    [Fact]
    public async Task Keeps_dispatching_while_batches_come_back_full_and_stops_at_the_first_short_one()
    {
        var dispatcher = Substitute.For<IOutboxDispatcher>();
        dispatcher.DispatchPendingAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(100, 100, 7);

        await Task_(dispatcher).ExecuteAsync(CancellationToken.None);

        await dispatcher.Received(3).DispatchPendingAsync(100, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_outbox_costs_one_pass()
    {
        var dispatcher = Substitute.For<IOutboxDispatcher>();
        dispatcher.DispatchPendingAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(0);

        await Task_(dispatcher).ExecuteAsync(CancellationToken.None);

        await dispatcher.Received(1).DispatchPendingAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_sustained_flood_is_cut_off_after_the_pass_cap()
    {
        var dispatcher = Substitute.For<IOutboxDispatcher>();
        dispatcher.DispatchPendingAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(100);

        await Task_(dispatcher).ExecuteAsync(CancellationToken.None);

        await dispatcher.Received(OutboxDispatchTask.MaxPassesPerRun).DispatchPendingAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>());
        OutboxDispatchTask.MaxPassesPerRun.ShouldBe(50);
    }

    private static OutboxDispatchTask Task_(IOutboxDispatcher dispatcher)
        => new(dispatcher, Options.Create(new OutboxOptions { BatchSize = 100 }), NullLogger<OutboxDispatchTask>.Instance);
}