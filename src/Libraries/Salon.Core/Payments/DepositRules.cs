namespace Salon.Core.Payments;

/// <summary>
/// Which deposit (glossary: "Zadatek") a booking asks for (ADR 0023). Pure. The most specific rule wins: the client's own
/// percentage, else the service's, else the salon's default. Zero at any level means "no deposit" at that level, so a
/// trusted client can be exempted and a single service can opt out of the salon's rule.
/// </summary>
public static class DepositRules
{
    public const decimal MaxPercent = 100m;

    /// <summary>The percentage that applies, between 0 and <see cref="MaxPercent"/>.</summary>
    public static decimal Percent(decimal salonDefault, decimal? serviceOverride, decimal? clientOverride)
        => Math.Clamp(clientOverride ?? serviceOverride ?? salonDefault, 0m, MaxPercent);

    /// <summary>The deposit for a visit at <paramref name="price"/>: to the penny, halves up, never above the price; 0 means none.</summary>
    public static decimal Amount(decimal price, decimal percent) => CancellationPolicy.FeeAmount(price, percent);

    public static bool IsPercentInRange(decimal? percent) => percent is null or (>= 0m and <= MaxPercent);
}