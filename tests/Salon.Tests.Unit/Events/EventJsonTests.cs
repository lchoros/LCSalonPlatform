using System.Text.Json;

using NodaTime;

using Salon.Services.Events;

namespace Salon.Tests.Unit.Events;

/// <summary>Outbox payload JSON: ISO text for NodaTime values, nulls preserved, malformed values rejected as JsonException.</summary>
public class EventJsonTests
{
    private sealed record Holder(Instant? When, LocalDateTime Local, LocalTime Time);

    [Fact]
    public void Noda_values_serialise_as_iso_text_and_nullable_values_as_null()
    {
        var json = JsonSerializer.Serialize(new Holder(null, new LocalDateTime(2026, 12, 24, 9, 30), new LocalTime(8, 5, 30)), EventJson.Options);

        json.ShouldBe("""{"when":null,"local":"2026-12-24T09:30:00","time":"08:05:30"}""");
    }

    [Fact]
    public void Noda_values_round_trip()
    {
        var original = new Holder(Instant.FromUtc(2026, 12, 24, 9, 0, 1), new LocalDateTime(2026, 1, 2, 3, 4, 5), new LocalTime(23, 59, 59));

        var restored = JsonSerializer.Deserialize<Holder>(JsonSerializer.Serialize(original, EventJson.Options), EventJson.Options);

        restored.ShouldBe(original);
    }

    [Theory]
    [InlineData("""{"when":12345,"local":"2026-12-24T09:30:00","time":"08:05:30"}""")]
    [InlineData("""{"when":"yesterday","local":"2026-12-24T09:30:00","time":"08:05:30"}""")]
    [InlineData("""{"when":null,"local":{"year":2026},"time":"08:05:30"}""")]
    public void Malformed_noda_values_are_json_errors_not_pattern_errors(string json)
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Holder>(json, EventJson.Options));
    }
}