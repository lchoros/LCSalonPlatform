using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Events;
using Salon.Services.Events;

namespace Salon.Tests.Unit.Events;

/// <summary>The real registration of the write side: publisher, eagerly built registry and every scanned consumer.</summary>
public class AddSalonEventsTests
{
    [Fact]
    public void Registers_every_consumer_found_in_the_given_assemblies_as_scoped()
    {
        var services = new ServiceCollection();

        services.AddSalonEvents(typeof(ThingHappened).Assembly);

        var consumers = services.Where(d => d.ServiceType == typeof(IConsumer<ThingHappened>)).ToList();
        consumers.Select(d => d.ImplementationType).ShouldBe([typeof(ThingHappenedConsumer), typeof(SecondThingHappenedConsumer)], ignoreOrder: true);
        consumers.ShouldAllBe(d => d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void Registers_the_outbox_publisher_and_a_ready_built_registry()
    {
        var services = new ServiceCollection();

        services.AddSalonEvents(typeof(ThingHappened).Assembly);

        services.ShouldContain(d => d.ServiceType == typeof(IEventPublisher) && d.ImplementationType == typeof(OutboxEventPublisher) && d.Lifetime == ServiceLifetime.Scoped);
        var registry = services.Single(d => d.ServiceType == typeof(IEventTypeRegistry));
        registry.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        registry.ImplementationInstance.ShouldBeOfType<EventTypeRegistry>().Resolve(typeof(ThingHappened).FullName!).ShouldBe(typeof(ThingHappened));
    }

    [Fact]
    public void Calling_it_twice_does_not_duplicate_anything()
    {
        var services = new ServiceCollection();

        services.AddSalonEvents(typeof(ThingHappened).Assembly);
        services.AddSalonEvents(typeof(ThingHappened).Assembly);

        services.Count(d => d.ServiceType == typeof(IConsumer<ThingHappened>)).ShouldBe(2);
        services.Count(d => d.ServiceType == typeof(IEventPublisher)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(IEventTypeRegistry)).ShouldBe(1);
    }

    [Fact]
    public async Task A_later_call_with_another_assembly_extends_the_registry()
    {
        var services = new ServiceCollection();
        services.AddSalonEvents();
        services.AddSalonEvents(typeof(ThingHappened).Assembly);
        await using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IEventTypeRegistry>();

        registry.Resolve(typeof(ThingHappened).FullName!).ShouldBe(typeof(ThingHappened));
    }

    [Fact]
    public void A_duplicate_event_name_fails_at_registration_not_at_first_dispatch()
    {
        var services = new ServiceCollection();
        services.AddSalonEvents(typeof(ThingHappened).Assembly);

        var ex = Should.Throw<InvalidOperationException>(
            () => services.AddSalonEvents(DynamicEventAssembly.WithDuplicateOf(typeof(ThingHappened), "Salon.Plugin.Rogue")));

        ex.Message.ShouldContain("Salon.Plugin.Rogue");
    }
}