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
}