using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Text;

namespace Salon.Services.Events;

/// <summary>
/// Serializer settings for outbox payloads: web defaults (camelCase, case-insensitive), no type-name handling, and
/// ISO text for the NodaTime values events carry: Instant (always UTC, "2026-12-24T09:00:00Z"), LocalDate, LocalTime,
/// LocalDateTime and Duration. Zoned or offset types are not supported on purpose: events carry an Instant plus a
/// zone id (CLAUDE.md rule 5).
/// </summary>
internal static class EventJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new NodaPatternConverter<Instant>(InstantPattern.ExtendedIso));
        options.Converters.Add(new NodaPatternConverter<LocalDate>(LocalDatePattern.Iso));
        options.Converters.Add(new NodaPatternConverter<LocalTime>(LocalTimePattern.ExtendedIso));
        options.Converters.Add(new NodaPatternConverter<LocalDateTime>(LocalDateTimePattern.ExtendedIso));
        options.Converters.Add(new NodaPatternConverter<Duration>(DurationPattern.Roundtrip));
        return options;
    }

    private sealed class NodaPatternConverter<T>(IPattern<T> pattern) : JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Expected an ISO string for {typeof(T).Name} but found {reader.TokenType}.");
            }

            var text = reader.GetString()!;
            var result = pattern.Parse(text);
            return result.Success ? result.Value : throw new JsonException($"'{text}' is not a valid {typeof(T).Name}.", result.Exception);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            => writer.WriteStringValue(pattern.Format(value));
    }
}