using System.Reflection;
using Salon.Core.Events;

namespace Salon.Services.Events;

internal sealed class EventTypeRegistry : IEventTypeRegistry
{
    private readonly HashSet<Type> _types;
    private readonly Dictionary<string, Type> _byName;

    public EventTypeRegistry(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var types = assemblies.Distinct().SelectMany(a => a.GetTypes()).Where(IsEventType).Distinct().ToList();
        var clash = types.GroupBy(t => t.FullName!, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (clash is not null)
        {
            throw new InvalidOperationException(
                $"Event type name '{clash.Key}' is defined in more than one assembly ({string.Join(", ", clash.Select(t => t.Assembly.GetName().Name))}). "
                + "Outbox rows store the name, so every event type needs a unique full name.");
        }

        _types = types.ToHashSet();
        _byName = types.ToDictionary(t => t.FullName!, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<Type> EventTypes => _types;

    public string NameOf(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return _types.Contains(eventType)
            ? eventType.FullName!
            : throw new InvalidOperationException(
                $"Event type '{eventType.FullName}' is not registered. Pass its assembly to AddSalonEvents so the outbox can dispatch it.");
    }

    public Type Resolve(string name)
        => name is not null && _byName.TryGetValue(name, out var type)
            ? type
            : throw new InvalidOperationException(
                $"Unknown event type '{name}'. Only events from assemblies passed to AddSalonEvents can be dispatched.");

    private static bool IsEventType(Type candidate)
        => candidate is { IsInterface: false, IsAbstract: false, IsGenericTypeDefinition: false } && typeof(IEvent).IsAssignableFrom(candidate);
}