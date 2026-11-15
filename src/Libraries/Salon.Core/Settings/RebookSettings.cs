using NodaTime;

namespace Salon.Core.Settings;

/// <summary>
/// How far ahead the desk offers the next visit when a client pays (ADR 0039): three one-tap choices, in weeks. The
/// defaults suit a salon that cuts and colours: four, six and eight weeks. 0 leaves a choice out.
/// </summary>
public sealed class RebookSettings : ISettings
{
    /// <summary>A year; nobody books a haircut further ahead than that.</summary>
    public const int MaxWeeks = 52;

    public int FirstWeeks { get; set; } = 4;

    public int SecondWeeks { get; set; } = 6;

    public int ThirdWeeks { get; set; } = 8;
}

/// <summary>Which choices the settings give and the day each one lands on. Pure.</summary>
public static class RebookRules
{
    /// <summary>The weeks to offer: each one once, shortest first, anything outside 1 to a year left out.</summary>
    public static IReadOnlyList<int> Choices(RebookSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new[] { settings.FirstWeeks, settings.SecondWeeks, settings.ThirdWeeks }
            .Where(weeks => weeks is > 0 and <= RebookSettings.MaxWeeks)
            .Distinct()
            .Order()
            .ToArray();
    }

    /// <summary>
    /// The day the next visit is offered on: the same weekday, so many weeks after the visit; counted from today when
    /// the visit was long ago, so the day is never in the past.
    /// </summary>
    public static LocalDate Day(LocalDate visitDay, LocalDate today, int weeks)
    {
        var day = visitDay.PlusWeeks(weeks);
        while (day <= today)
        {
            day = day.PlusWeeks(1);
        }

        return day;
    }
}