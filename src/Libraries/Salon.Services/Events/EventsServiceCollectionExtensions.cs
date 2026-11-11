using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Salon.Core.Events;

namespace Salon.Services.Events;

public static class EventsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the outbox publisher (ADR 0007), adds <paramref name="eventAssemblies"/> (plus Core and Services) to
    /// the event type registry and registers every <see cref="IConsumer{TEvent}"/> found in them as scoped.
    /// Safe to call more than once: later calls only extend the registry and add new consumers, so plugins can register
    /// their own events after the host. The registry is built eagerly, so a duplicate event name fails right here.
    /// </summary>
    public static IServiceCollection AddSalonEvents(this IServiceCollection services, params Assembly[] eventAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(eventAssemblies);

        services.TryAddScoped<IEventPublisher, OutboxEventPublisher>();

        var assemblies = eventAssemblies.Append(typeof(IEvent).Assembly).Append(typeof(EventTypeRegistry).Assembly).Distinct();
        foreach (var assembly in assemblies)
        {
            if (services.Any(d => d.ImplementationInstance is EventAssemblySource source && source.Assembly == assembly))
            {
                continue;
            }

            services.AddSingleton(new EventAssemblySource(assembly));
            foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }))
            {
                foreach (var consumerInterface in type.GetInterfaces().Where(IsConsumer))
                {
                    services.TryAddEnumerable(ServiceDescriptor.Scoped(consumerInterface, type));
                }
            }
        }

        var scanned = services.Select(d => d.ImplementationInstance).OfType<EventAssemblySource>().Select(s => s.Assembly);
        services.Replace(ServiceDescriptor.Singleton<IEventTypeRegistry>(new EventTypeRegistry(scanned)));
        return services;
    }

    private static bool IsConsumer(Type candidate)
        => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IConsumer<>);

    /// <summary>One registration per scanned assembly; the registry singleton is rebuilt from all of them on every call.</summary>
    private sealed class EventAssemblySource(Assembly assembly)
    {
        public Assembly Assembly { get; } = assembly;
    }
}