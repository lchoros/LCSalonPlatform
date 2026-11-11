using Salon.Core.Events;
using Salon.Services.Events;

namespace Salon.Tests.Unit.Events;

/// <summary>Stable names ↔ event types, built only from scanned assemblies; stored strings never reach Type.GetType.</summary>
public class EventTypeRegistryTests
{
    private static readonly EventTypeRegistry Registry = new([typeof(EventTypeRegistryTests).Assembly]);

    [Fact]
    public void Names_are_the_clr_full_name()
    {
        Registry.NameOf(typeof(ThingHappened)).ShouldBe(typeof(ThingHappened).FullName);
        Registry.NameOf(typeof(ThingHappened)).ShouldBe("Salon.Tests.Unit.Events.ThingHappened");
    }

    [Fact]
    public void Resolves_a_known_name_to_its_type()
    {
        Registry.Resolve("Salon.Tests.Unit.Events.ThingHappened").ShouldBe(typeof(ThingHappened));
        Registry.Resolve(Registry.NameOf(typeof(NothingListens))).ShouldBe(typeof(NothingListens));
    }

    [Theory]
    [InlineData("Salon.Tests.Unit.Events.Nope")]
    [InlineData("System.IO.FileInfo")]
    [InlineData("Salon.Tests.Unit.Events.NotAnEvent")]
    [InlineData("")]
    public void Unknown_or_unregistered_names_throw_instead_of_falling_back_to_Type_GetType(string name)
    {
        Should.Throw<InvalidOperationException>(() => Registry.Resolve(name));
    }

    [Fact]
    public void Naming_a_type_that_was_not_scanned_throws()
    {
        var empty = new EventTypeRegistry([]);

        Should.Throw<InvalidOperationException>(() => empty.NameOf(typeof(ThingHappened)));
    }

    [Fact]
    public void Registers_only_concrete_event_types_from_the_given_assemblies()
    {
        Registry.EventTypes.ShouldContain(typeof(ThingHappened));
        Registry.EventTypes.ShouldContain(typeof(NothingListens));
        Registry.EventTypes.ShouldNotContain(typeof(IEvent));
        Registry.EventTypes.ShouldNotContain(typeof(NotAnEvent));
    }

    [Fact]
    public void The_same_full_name_in_two_assemblies_is_refused_naming_both()
    {
        var duplicate = DynamicEventAssembly.WithDuplicateOf(typeof(ThingHappened));

        var ex = Should.Throw<InvalidOperationException>(() => new EventTypeRegistry([typeof(ThingHappened).Assembly, duplicate]));

        ex.Message.ShouldContain("Salon.Tests.Unit.Events.ThingHappened");
        ex.Message.ShouldContain("Salon.Tests.Unit");
        ex.Message.ShouldContain("Salon.Tests.Unit.DynamicEvents");
    }

    [Fact]
    public void Scanning_the_same_assembly_twice_is_not_a_duplicate()
    {
        var registry = new EventTypeRegistry([typeof(ThingHappened).Assembly, typeof(ThingHappened).Assembly]);

        registry.Resolve(typeof(ThingHappened).FullName!).ShouldBe(typeof(ThingHappened));
    }
}