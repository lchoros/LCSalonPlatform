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

    /// <summary>
    /// Whether the public booking pages offer "tell me when a time comes up" (ADR 0055). Off until the owner switches
    /// it on: it lets people who are not clients yet leave their name and number.
    /// </summary>
    public bool PublicWaitlist { get; set; }

    /// <summary>
    /// Whether the first client waiting is sent a text when a visit is cancelled and its time can be booked again
    /// (ADR 0056). Off until the owner switches it on: every text costs the salon money.
    /// </summary>
    public bool TellWaitlistWhenFreed { get; set; }

    /// <summary>
    /// For how many minutes the freed time is kept for the client who was told (ADR 0065): off the booking pages, and
    /// theirs with one tap. 0 means the time is not held and whoever books first has it (ADR 0056). Only matters while
    /// <see cref="TellWaitlistWhenFreed"/> is on.
    /// </summary>
    public int HoldFreedTimeMinutes { get; set; } = 30;

    /// <summary>0 means no deposit. Applied as a percentage of the service price.</summary>
    public decimal DefaultDepositPercent { get; set; }
}