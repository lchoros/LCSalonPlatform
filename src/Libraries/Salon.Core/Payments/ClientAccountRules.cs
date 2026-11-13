namespace Salon.Core.Payments;

/// <summary>The families of lines on a client's account (ADR 0022, ADR 0023): what the client owes, what closes it, and deposits. Pure.</summary>
public static class ClientAccountRules
{
    public static bool IsFee(ClientAccountEntryKind kind)
        => kind is ClientAccountEntryKind.NoShowFee or ClientAccountEntryKind.LateCancellationFee;

    /// <summary>A waiver or a payment: the line that closes a fee.</summary>
    public static bool IsSettlement(ClientAccountEntryKind kind)
        => kind is ClientAccountEntryKind.FeeWaived or ClientAccountEntryKind.FeePayment;

    /// <summary>A deposit received or given back (ADR 0023): money held for a visit, not a debt.</summary>
    public static bool IsDeposit(ClientAccountEntryKind kind)
        => kind is ClientAccountEntryKind.DepositPaid or ClientAccountEntryKind.DepositReturned;

    /// <summary>Money taken for a visit that took place (ADR 0024): neither a debt nor held, simply the bill paid.</summary>
    public static bool IsVisitPayment(ClientAccountEntryKind kind) => kind is ClientAccountEntryKind.VisitPayment;
}