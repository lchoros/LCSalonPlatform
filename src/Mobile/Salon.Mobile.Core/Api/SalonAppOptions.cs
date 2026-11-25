namespace Salon.Mobile.Core.Api;

/// <summary>Where the app finds its salon: the salon's own host, fixed when the app is built (ADR 0097).</summary>
public sealed class SalonAppOptions
{
    /// <summary>The salon's host, e.g. https://studio.example.com/. The API answers under /api/v1/booking on it.</summary>
    public required Uri BaseAddress { get; init; }

    /// <summary>How long one call may take before the app treats the network as gone.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);
}