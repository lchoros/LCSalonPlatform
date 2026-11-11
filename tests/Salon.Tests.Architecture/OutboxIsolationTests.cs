using NetArchTest.Rules;

namespace Salon.Tests.Architecture;

/// <summary>
/// ADR 0007: outbox_messages is exempt from the tenant filter and RLS (platform dispatcher, payload may hold client
/// data), so only Salon.Services.Events and the Data mapping may touch the entity. Slices, plugins and other services
/// go through IEventPublisher / IConsumer.
/// </summary>
public class OutboxIsolationTests
{
    private const string OutboxEntity = "Salon.Core.Events.OutboxMessage";

    [Fact]
    public void The_host_the_framework_and_the_plugins_never_reference_the_outbox_entity()
    {
        foreach (var assembly in new[] { Assemblies.Web, Assemblies.WebFramework }.Concat(Assemblies.Plugins))
        {
            var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOn(OutboxEntity).GetResult();

            result.IsSuccessful.ShouldBeTrue($"{assembly.GetName().Name}: {LayerDependencyTests.Describe(result)}");
        }
    }

    [Fact]
    public void Services_outside_the_events_namespace_never_reference_the_outbox_entity()
    {
        var result = Types.InAssembly(Assemblies.Services)
            .That().DoNotResideInNamespaceStartingWith("Salon.Services.Events")
            .ShouldNot().HaveDependencyOn(OutboxEntity)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(LayerDependencyTests.Describe(result));
    }
}