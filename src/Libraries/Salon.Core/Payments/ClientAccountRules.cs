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

    /// <summary>
    /// Money taken for a visit that took place (ADR 0024): neither a debt nor held, simply the bill paid, in one
    /// payment or with a part paid another way first (ADR 0061).
    /// </summary>
    public static bool IsVisitPayment(ClientAccountEntryKind kind) => kind is ClientAccountEntryKind.VisitPayment or ClientAccountEntryKind.PartPayment;

    /// <summary>Money taken for products on a visit's bill, or given back for ones returned (ADR 0037, ADR 0040): retail, kept apart from what the visit itself cost.</summary>
    public static bool IsProductSale(ClientAccountEntryKind kind) => kind is ClientAccountEntryKind.ProductSale or ClientAccountEntryKind.ProductReturn;

    /// <summary>A gift voucher sold, used toward a visit, or given back (ADR 0057): money taken ahead of the visit it pays for.</summary>
    public static bool IsVoucher(ClientAccountEntryKind kind)
        => kind is ClientAccountEntryKind.VoucherSale or ClientAccountEntryKind.VoucherRedeemed or ClientAccountEntryKind.VoucherRefund;

    /// <summary>
    /// One of the two lines that move a line's money from the method it was recorded under to the one it was paid by
    /// (ADR 0064): no new money, only where the till counts it.
    /// </summary>
    public static bool IsMethodCorrection(ClientAccountEntryKind kind)
        => kind is ClientAccountEntryKind.MethodCorrectionIn or ClientAccountEntryKind.MethodCorrectionOut;
}