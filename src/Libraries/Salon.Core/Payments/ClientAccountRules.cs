namespace Salon.Core.Payments;

/// <summary>The two families of lines on a client's account (ADR 0022): what the client owes, and what closes it. Pure.</summary>
public static class ClientAccountRules
{
    public static bool IsFee(ClientAccountEntryKind kind)
        => kind is ClientAccountEntryKind.NoShowFee or ClientAccountEntryKind.LateCancellationFee;

    /// <summary>A waiver or a payment: the line that closes a fee.</summary>
    public static bool IsSettlement(ClientAccountEntryKind kind)
        => kind is ClientAccountEntryKind.FeeWaived or ClientAccountEntryKind.FeePayment;
}