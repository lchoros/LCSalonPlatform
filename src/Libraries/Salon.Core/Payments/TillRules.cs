namespace Salon.Core.Payments;

/// <summary>
/// How a bill and a day's till add up (ADR 0024). Pure. The till is not a second set of books: every line on a client's
/// account that carries a payment method is money that changed hands, and the day is their sum.
/// </summary>
public static class TillRules
{
    /// <summary>
    /// What is left to pay for a visit: the booked price less the deposit the salon holds for it, never below zero.
    /// </summary>
    public static decimal Outstanding(decimal price, decimal depositHeld) => Math.Max(0m, price - Math.Max(0m, depositHeld));

    /// <summary>
    /// +1 for money that came in (a deposit, a fee paid, a visit paid), -1 for money that went out (a deposit returned),
    /// 0 for lines that moved no money (a fee added or waived).
    /// </summary>
    public static int Direction(ClientAccountEntryKind kind) => kind switch
    {
        ClientAccountEntryKind.DepositPaid or ClientAccountEntryKind.FeePayment or ClientAccountEntryKind.VisitPayment => 1,
        ClientAccountEntryKind.DepositReturned => -1,
        _ => 0,
    };

    /// <summary>The day's net amount per method, in the order of <see cref="PaymentMethod"/>; methods nobody used are left out.</summary>
    public static IReadOnlyList<(PaymentMethod Method, decimal Amount)> Totals(IEnumerable<(ClientAccountEntryKind Kind, PaymentMethod Method, decimal Amount)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines
            .Where(l => Direction(l.Kind) != 0)
            .GroupBy(l => l.Method)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Sum(l => Direction(l.Kind) * l.Amount)))
            .ToArray();
    }

    /// <summary>What the drawer should hold from the day's lines: cash in less cash out.</summary>
    public static decimal ExpectedCash(IEnumerable<(ClientAccountEntryKind Kind, PaymentMethod Method, decimal Amount)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Where(l => l.Method == PaymentMethod.Cash).Sum(l => Direction(l.Kind) * l.Amount);
    }

    /// <summary>A counted amount is not negative and has at most two decimals.</summary>
    public static bool IsCountable(decimal amount) => amount >= 0m && amount <= MaxCounted && decimal.Round(amount, 2) == amount;

    /// <summary>More than any salon drawer holds; keeps a slip of the finger out of the books.</summary>
    public const decimal MaxCounted = 1_000_000m;
}