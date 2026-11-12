namespace Salon.Core.Settings;

/// <summary>Per-salon online booking rules. The Booking module (M1) reads these through ISettingService.</summary>
public sealed class BookingSettings : ISettings
{
    /// <summary>Clients cannot book a slot that starts sooner than this.</summary>
    public int MinNoticeMinutes { get; set; } = 60;

    /// <summary>How far ahead the public calendar is open: the last bookable day is today plus this many days.</summary>
    public int MaxAdvanceDays { get; set; } = 60;

    /// <summary>Slots start every this many minutes of the clock (ADR 0012).</summary>
    public int SlotStepMinutes { get; set; } = 15;

    /// <summary>Minutes kept free after every appointment (tidy-up); not part of the booked range (ADR 0012).</summary>
    public int BufferAfterMinutes { get; set; }

    public bool RequireSmsVerification { get; set; } = true;

    /// <summary>0 means no deposit. Applied as a percentage of the service price.</summary>
    public decimal DefaultDepositPercent { get; set; }
}