using System.Text.Json;

using NodaTime;

using Salon.Services.Events;

namespace Salon.Tests.Unit.Events;

/// <summary>The pure half of the publisher: what a row looks like and that it deserialises back to the same event.</summary>
public class OutboxMessageFactoryTests
{
    private static readonly EventTypeRegistry Registry = new([typeof(OutboxMessageFactoryTests).Assembly]);
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 11, 10, 30, 15);
    private static readonly Guid TenantId = Guid.Parse("0190a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b");

    private static readonly ThingHappened Event = new(
        Guid.Parse("018f3f1e-2b7a-7c3e-9d1a-5b6c7d8e9f00"),
        "Zażółć gęślą jaźń",
        12.50m,
        Instant.FromUtc(2026, 12, 24, 9, 0),
        new LocalDate(2026, 12, 24),
        Duration.FromMinutes(45));

    [Fact]
    public void Create_stamps_tenant_type_time_and_a_fresh_retry_state()
    {
        var message = OutboxMessageFactory.Create(Event, TenantId, Now, Registry);

        message.Id.ShouldNotBe(Guid.Empty);
        message.TenantId.ShouldBe(TenantId);
        message.EventType.ShouldBe("Salon.Tests.Unit.Events.ThingHappened");
        message.OccurredAt.ShouldBe(Now);
        message.Attempts.ShouldBe(0);
        message.ProcessedAt.ShouldBeNull();
        message.FailedAt.ShouldBeNull();
        message.NextAttemptAt.ShouldBeNull();
        message.LastError.ShouldBeNull();
    }

    [Fact]
    public void A_platform_event_has_no_tenant()
    {
        OutboxMessageFactory.Create(new NothingListens(Guid.NewGuid()), null, Now, Registry).TenantId.ShouldBeNull();
    }

    [Fact]
    public void Payload_is_camel_case_json_with_iso_noda_time_values()
    {
        var message = OutboxMessageFactory.Create(Event, TenantId, Now, Registry);

        using var json = JsonDocument.Parse(message.Payload);
        var root = json.RootElement;
        root.GetProperty("thingId").GetGuid().ShouldBe(Event.ThingId);
        root.GetProperty("name").GetString().ShouldBe("Zażółć gęślą jaźń");
        root.GetProperty("amount").GetDecimal().ShouldBe(12.50m);
        root.GetProperty("at").GetString().ShouldBe("2026-12-24T09:00:00Z");
        root.GetProperty("day").GetString().ShouldBe("2026-12-24");
        root.GetProperty("length").GetString().ShouldBe("0:00:45:00");
        root.TryGetProperty("$type", out _).ShouldBeFalse();
    }

    [Fact]
    public void Deserialize_round_trips_the_event()
    {
        var message = OutboxMessageFactory.Create(Event, TenantId, Now, Registry);

        var restored = OutboxMessageFactory.Deserialize(message, Registry);

        restored.ShouldBeOfType<ThingHappened>().ShouldBe(Event);
    }

    [Fact]
    public void Deserialize_refuses_an_unknown_event_type()
    {
        var message = OutboxMessageFactory.Create(Event, TenantId, Now, Registry);
        message.EventType = "System.Diagnostics.Process";

        Should.Throw<InvalidOperationException>(() => OutboxMessageFactory.Deserialize(message, Registry));
    }

    [Fact]
    public void Deserialize_refuses_a_payload_that_is_not_an_object()
    {
        var message = OutboxMessageFactory.Create(Event, TenantId, Now, Registry);
        message.Payload = "null";

        Should.Throw<InvalidOperationException>(() => OutboxMessageFactory.Deserialize(message, Registry));
    }

    [Fact]
    public void Create_refuses_an_event_type_the_registry_does_not_know()
    {
        var empty = new EventTypeRegistry([]);

        Should.Throw<InvalidOperationException>(() => OutboxMessageFactory.Create(Event, TenantId, Now, empty));
    }
}