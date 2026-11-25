using Microsoft.Extensions.Options;
using Salon.Core.Appointments;
using Salon.Core.Payments;
using Salon.Web.Framework.Infrastructure;

namespace Salon.Web.Framework.Booking;

/// <summary>
/// The further services of a visit booked as several (ADR 0095), as the public booking pages carry them from step to
/// step in the address: <c>/book/{first}?also={second},{third}</c>. The first service stays in the route, so every
/// address from before still means what it meant.
/// </summary>
public interface IVisitBasket
{
    /// <summary>
    /// Whether a client may put more than one service into a booking here: the feature is on, and no payment gateway
    /// takes deposits online (its pay step knows one appointment; ADR 0082).
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// The further services an address names, in order: each once, never the first one again, at most
    /// <see cref="VisitRules.MaxServices"/> with the first. Anything that cannot be read is left out; empty while
    /// <see cref="IsOpen"/> is false.
    /// </summary>
    IReadOnlyList<Guid> Read(string? also, Guid first);
}

public sealed class VisitBasket(IOptionsMonitor<FeaturesOptions> features, IOnlineDepositCheckout deposits) : IVisitBasket
{
    /// <summary>The name of the value in the address and in the details form.</summary>
    public const string Parameter = "also";

    public bool IsOpen => features.CurrentValue.SeveralServices && !deposits.IsAvailable;

    public IReadOnlyList<Guid> Read(string? also, Guid first)
    {
        if (!IsOpen || string.IsNullOrWhiteSpace(also))
        {
            return [];
        }

        var extras = new List<Guid>();
        foreach (var part in also.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(part, out var id) && id != first && !extras.Contains(id) && extras.Count < VisitRules.MaxServices - 1)
            {
                extras.Add(id);
            }
        }

        return extras;
    }

    /// <summary>The value as it is written into an address or a form: ids joined by commas; null when there are none.</summary>
    public static string? Write(IEnumerable<Guid> extras)
    {
        ArgumentNullException.ThrowIfNull(extras);
        var joined = string.Join(',', extras);
        return joined.Length == 0 ? null : joined;
    }

    /// <summary>The piece appended to an address that already has a query: <c>&amp;also=…</c>, or nothing.</summary>
    public static string Query(IEnumerable<Guid> extras) => Write(extras) is { } value ? $"&{Parameter}={value}" : string.Empty;
}