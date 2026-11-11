namespace Salon.Services.Events;

/// <summary>
/// Stable name ↔ event type, built only from the assemblies passed to <c>AddSalonEvents</c>. The outbox stores the
/// name, never an assembly-qualified CLR string, and stored names never reach <c>Type.GetType</c>.
/// </summary>
/// <remarks>
/// Only concrete, non-generic types are registered: a closed generic event (<c>Wrapper&lt;Thing&gt;</c>) is rejected
/// by <see cref="NameOf"/> because its name would embed another type's identity. A full name defined in two scanned
/// assemblies fails registration. Scanning uses <c>Assembly.GetTypes()</c>, which throws
/// <see cref="System.Reflection.ReflectionTypeLoadException"/> for an assembly with unresolvable references, exactly
/// like <c>AddSalonPipeline</c>; a broken plugin therefore fails at start-up, not at first dispatch.
/// </remarks>
public interface IEventTypeRegistry
{
    /// <summary>Every concrete event type found in the scanned assemblies.</summary>
    IReadOnlyCollection<Type> EventTypes { get; }

    /// <summary>The name stored for this event type (its CLR full name); throws if the type was not scanned.</summary>
    string NameOf(Type eventType);

    /// <summary>The event type for a stored name; throws for anything that was not scanned.</summary>
    Type Resolve(string name);
}