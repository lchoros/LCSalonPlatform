namespace Salon.Core.Payments;

/// <summary>
/// How a bill and a day's till add up (ADR 0024). Pure. The till is not a second set of books: every line on a client's
/// account that carries a payment method is money that changed hands, and the day is their sum.
/// </summary>
public static class TillRules
{
    /// <summary>
    /// What is left to pay for a visit: the booked price less what the salon already holds toward it (a deposit, a
    /// gift voucher used on it, ADR 0057), never below zero.
    /// </summary>
    public static decimal Outstanding(decimal price, decimal depositHeld) => Math.Max(0m, price - Math.Max(0m, depositHeld));

    /// <summary>
    /// +1 for money that came in (a deposit, a fee paid, a visit paid, products or a gift voucher sold), -1 for money
    /// that went out (a deposit returned, products returned, a voucher given back), 0 for lines that moved no money (a
    /// fee added or waived, a voucher used: its money came in when it was sold).
    /// </summary>
    public static int Direction(ClientAccountEntryKind kind) => kind switch
    {
        ClientAccountEntryKind.DepositPaid or ClientAccountEntryKind.FeePayment or ClientAccountEntryKind.VisitPayment or ClientAccountEntryKind.ProductSale
            or ClientAccountEntryKind.VoucherSale => 1,
        ClientAccountEntryKind.DepositReturned or ClientAccountEntryKind.ProductReturn or ClientAccountEntryKind.VoucherRefund => -1,
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

    /// <summary>
    /// The net amount per method with each of the salon's own methods on its own line (ADR 0060): the built-in
    /// methods first in the order of <see cref="PaymentMethod"/>, then the salon's own by name. A method the salon
    /// renamed is one line, under the name on its latest line; give the lines oldest first.
    /// </summary>
    public static IReadOnlyList<MethodNet> TotalsByMethod(IEnumerable<MethodLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines
            .Where(l => Direction(l.Kind) != 0)
            .GroupBy(l => (l.Method, l.CustomMethodId))
            .Select(g => new MethodNet(g.Key.Method, g.Key.CustomMethodId, g.Select(l => l.MethodName).LastOrDefault(name => name is not null), g.Sum(l => Direction(l.Kind) * l.Amount)))
            .OrderBy(t => t.Method)
            .ThenBy(t => t.MethodName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.CustomMethodId)
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
/// <summary>One line that carries a method, as the till adds it up (ADR 0060).</summary>
/// <param name="CustomMethodId">Which of the salon's own methods; null for a built-in one.</param>
/// <param name="MethodName">What the salon called its own method on the day of the line.</param>
public readonly record struct MethodLine(ClientAccountEntryKind Kind, PaymentMethod Method, Guid? CustomMethodId, string? MethodName, decimal Amount);

/// <summary>What came in less what went out by one method.</summary>
public readonly record struct MethodNet(PaymentMethod Method, Guid? CustomMethodId, string? MethodName, decimal Amount);