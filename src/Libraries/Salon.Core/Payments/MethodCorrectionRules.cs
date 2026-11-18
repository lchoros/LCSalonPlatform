namespace Salon.Core.Payments;

/// <summary>
/// Putting right a line that was recorded under the wrong method (ADR 0064): "I pressed cash, it was card". The line
/// itself is never edited. Two lines are added that point at it: one takes its amount off the method it was recorded
/// under, the other puts it on the method it was really paid by. The day's takings stay what they were and each
/// method's total becomes right. Pure.
/// </summary>
public static class MethodCorrectionRules
{
    /// <summary>A line can be put right when it moved money and is not itself half of a correction.</summary>
    public static bool CanCorrect(ClientAccountEntryKind kind)
        => TillRules.Direction(kind) != 0 && !ClientAccountRules.IsMethodCorrection(kind);

    /// <summary>
    /// The kinds of the two lines that correct a line of <paramref name="original"/> kind: <c>Undo</c> carries the
    /// method the money was recorded under and cancels it, <c>Restate</c> carries the method it was paid by.
    /// </summary>
    public static (ClientAccountEntryKind Undo, ClientAccountEntryKind Restate) Pair(ClientAccountEntryKind original)
    {
        if (!CanCorrect(original))
        {
            throw new ArgumentOutOfRangeException(nameof(original), original, "Only a line that moved money can be corrected.");
        }

        return TillRules.Direction(original) > 0
            ? (ClientAccountEntryKind.MethodCorrectionOut, ClientAccountEntryKind.MethodCorrectionIn)
            : (ClientAccountEntryKind.MethodCorrectionIn, ClientAccountEntryKind.MethodCorrectionOut);
    }

    /// <summary>
    /// Whether a correction line says how a line of <paramref name="original"/> kind was paid: the half that moves
    /// money the same way as the line it corrects. The latest such line is the method the line stands at.
    /// </summary>
    public static bool Restates(ClientAccountEntryKind original, ClientAccountEntryKind correction)
        => ClientAccountRules.IsMethodCorrection(correction) && CanCorrect(original) && TillRules.Direction(correction) == TillRules.Direction(original);

    /// <summary>
    /// Whether a line of <paramref name="original"/> kind can stand at <paramref name="method"/>: a gift voucher is
    /// not bought or given back with another voucher (ADR 0057), corrected or not.
    /// </summary>
    public static bool Allows(ClientAccountEntryKind original, PaymentChoice method)
        => method.IsWellFormed && (!ClientAccountRules.IsVoucher(original) || GiftVoucherRules.CanBePaidWith(method));
}