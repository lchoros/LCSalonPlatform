namespace Salon.Core.Staff;

/// <summary>
/// Commission (ADR 0059): a percent of what a person's work and sales brought in, paid to them by the salon. Pure.
/// </summary>
public static class CommissionRules
{
    public const decimal MaxPercent = 100m;

    /// <summary>A rate is 0 to 100 with at most two decimals (12.5, 33.33). Null means "not paid this way".</summary>
    public static bool IsRate(decimal? percent)
        => percent is not { } value || (value >= 0m && value <= MaxPercent && decimal.Round(value, 2) == value);

    /// <summary>What a rate earns on an amount, to the penny, half a penny up. Nothing without a rate.</summary>
    public static decimal? Earned(decimal amount, decimal? percent)
        => percent is { } rate ? decimal.Round(amount * rate / 100m, 2, MidpointRounding.AwayFromZero) : null;

    /// <summary>
    /// Commission on services and on products together; null only when the person has neither rate. A rate with
    /// nothing to earn it on is zero, not null: the person is paid by commission and earned none.
    /// </summary>
    public static decimal? Total(decimal services, decimal? serviceRate, decimal products, decimal? productRate)
        => serviceRate is null && productRate is null
            ? null
            : (Earned(services, serviceRate) ?? 0m) + (Earned(products, productRate) ?? 0m);

    /// <summary>
    /// What a set of amounts earns, each at its own rate, rounded once at the end so a hundred small visits do not
    /// lose or gain a penny each. Amounts without a rate earn nothing; null when none has a rate.
    /// </summary>
    public static decimal? Earned(IEnumerable<(decimal Amount, decimal? Rate)> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        decimal? sum = null;
        foreach (var (amount, rate) in items)
        {
            if (rate is { } percent)
            {
                sum = (sum ?? 0m) + (amount * percent / 100m);
            }
        }

        return sum is { } total ? decimal.Round(total, 2, MidpointRounding.AwayFromZero) : null;
    }

    /// <summary>
    /// The rate a visit earns at (ADR 0059): the one kept on it when it was completed, even "none"; today's rate for a
    /// visit completed before rates were kept, or not completed yet.
    /// </summary>
    public static decimal? RateFor(bool kept, decimal? keptRate, decimal? todaysRate) => kept ? keptRate : todaysRate;
}