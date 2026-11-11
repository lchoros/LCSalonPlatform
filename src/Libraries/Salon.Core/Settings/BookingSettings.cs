namespace Salon.Core.Settings;

/// <summary>Per-salon online booking rules. The Booking module (M1) reads these through ISettingService.</summary>
public sealed class BookingSettings : ISettings
{
    /// <summary>Clients cannot book a slot that starts sooner than this.</summary>
    public int MinNoticeMinutes { get; set; } = 60;

    /// <summary>How far ahead the public calendar is open.</summary>
    public int MaxAdvanceDays { get; set; } = 60;

    public bool RequireSmsVerification { get; set; } = true;

    /// <summary>0 means no deposit. Applied as a percentage of the service price.</summary>
    public decimal DefaultDepositPercent { get; set; }
}