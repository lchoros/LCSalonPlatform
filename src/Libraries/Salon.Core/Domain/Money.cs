namespace Salon.Core.Domain;

/// <summary>Money is always a decimal amount plus an ISO 4217 currency code (GBP, PLN). Never a float.</summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);

    public Money Add(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Cannot add {other.Currency} to {Currency}.");
        }

        return this with { Amount = Amount + other.Amount };
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}