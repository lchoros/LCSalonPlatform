using System.Reflection;
using System.Reflection.Emit;
using NodaTime;
using Salon.Core.Events;

namespace Salon.Tests.Unit.Events;

/// <summary>Sample events and consumers used to exercise the registry and the DI scan through AddSalonEvents(typeof(ThingHappened).Assembly).</summary>
public sealed record ThingHappened(Guid ThingId, string Name, decimal Amount, Instant At, LocalDate Day, Duration Length) : IEvent;

public sealed class ThingHappenedConsumer : IConsumer<ThingHappened>
{
    public Task HandleAsync(ThingHappened domainEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class SecondThingHappenedConsumer : IConsumer<ThingHappened>
{
    public Task HandleAsync(ThingHappened domainEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Has no consumer on purpose.</summary>
public sealed record NothingListens(Guid Id) : IEvent;

/// <summary>Not an event; the registry must ignore it.</summary>
public sealed record NotAnEvent(Guid Id);

/// <summary>Builds throw-away assemblies so a test can present the registry with a second type of the same full name.</summary>
internal static class DynamicEventAssembly
{
    public static Assembly WithDuplicateOf(Type eventType, string assemblyName = "Salon.Tests.Unit.DynamicEvents")
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("main").DefineType(eventType.FullName!, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);
        type.AddInterfaceImplementation(typeof(IEvent));
        type.CreateType();
        return assembly;
    }
}