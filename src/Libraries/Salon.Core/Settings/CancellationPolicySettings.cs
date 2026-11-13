namespace Salon.Core.Settings;

/// <summary>
/// The salon's cancellation policy (ADR 0022): until when a client may change or cancel free of charge, and what a late
/// cancellation or a missed visit costs. The defaults are "no policy": free until the visit starts and no fees, so a
/// salon that never opens the page behaves as before.
/// </summary>
public sealed class CancellationPolicySettings : ISettings
{
    /// <summary>A week; a longer notice than that is not a cancellation policy any client would accept.</summary>
    public const int MaxFreeChangeHours = 168;

    /// <summary>
    /// A client may change or cancel free of charge until this many hours before the visit. 0 means until it starts.
    /// </summary>
    public int FreeChangeHours { get; set; }

    /// <summary>Percent of the booked price a cancellation inside the notice period may cost. 0 means no fee.</summary>
    public decimal LateCancellationFeePercent { get; set; }

    /// <summary>Percent of the booked price a missed visit may cost. 0 means no fee.</summary>
    public decimal NoShowFeePercent { get; set; }
}