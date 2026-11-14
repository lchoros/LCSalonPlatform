using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Domain;
using Salon.Core.Settings;

namespace Salon.Core.Payments;

/// <summary>
/// The salon's cancellation policy as rules (ADR 0022). Pure: no clock, no database. The policy only ever proposes a fee;
/// a person at the salon decides whether it is added to the client's account.
/// </summary>
public static class CancellationPolicy
{
    /// <summary>
    /// True when <paramref name="at"/> is past the last moment for a free change or cancellation of a visit starting at
    /// <paramref name="start"/>. With no notice period (0 hours) nothing is ever late: the visit's own start is the limit
    /// and the booking rules already guard it.
    /// </summary>
    public static bool IsInsideNotice(Instant start, Instant at, int freeChangeHours)
        => freeChangeHours > 0 && at > start - Duration.FromHours(freeChangeHours);

    /// <summary>
    /// <paramref name="percent"/> of <paramref name="price"/>, to the penny (halves round up), never more than the price
    /// and never below zero.
    /// </summary>
    public static decimal FeeAmount(decimal price, decimal percent)
    {
        if (price <= 0m || percent <= 0m)
        {
            return 0m;
        }

        return Math.Min(price, Math.Round(price * percent / 100m, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// The fee the policy allows for a visit that ended in <paramref name="status"/>: a no-show fee for a missed visit, a
    /// late-cancellation fee for a visit cancelled (by either side: the salon records a client's phone call as its own
    /// cancellation) inside the notice period. Null when the policy sets none or the amount comes to nothing.
    /// </summary>
    public static FeeProposal? Propose(AppointmentStatus status, Instant start, Instant? cancelledAt, Money price, CancellationPolicySettings policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var (kind, percent) = status switch
        {
            AppointmentStatus.NoShow => (ClientAccountEntryKind.NoShowFee, policy.NoShowFeePercent),
            AppointmentStatus.CancelledByClient or AppointmentStatus.CancelledBySalon
                when cancelledAt is { } at && IsInsideNotice(start, at, policy.FreeChangeHours)
                => (ClientAccountEntryKind.LateCancellationFee, policy.LateCancellationFeePercent),
            _ => (default(ClientAccountEntryKind), 0m),
        };

        var amount = FeeAmount(price.Amount, percent);
        return amount > 0m ? new FeeProposal(kind, price with { Amount = amount }, percent) : null;
    }
}

/// <param name="Percent">The policy's percentage the amount came from, for the preview.</param>
public sealed record FeeProposal(ClientAccountEntryKind Kind, Money Amount, decimal Percent);