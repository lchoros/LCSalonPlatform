namespace Salon.Core.Settings;

/// <summary>
/// The day's numbers by email to the salon's owners (ADR 0050). Off until the owner switches it on. The time is on
/// the salon's own clock.
/// </summary>
public sealed class DailySummarySettings : ISettings
{
    public const int MinutesPerDay = 1440;

    public bool Enabled { get; set; }

    /// <summary>Minute of the day the summary goes out (default 20:00), on the salon's clock.</summary>
    public int SendAtMinute { get; set; } = 20 * 60;
}