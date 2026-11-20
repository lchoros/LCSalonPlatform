using Salon.Core.Domain;

namespace Salon.Core.Payments;

/// <summary>
/// What the ledger does with a deposit a gateway says was paid online (ADR 0082). Pure. The same session may be
/// reported any number of times, by the client's browser coming back and by the gateway's webhook: it is one line.
/// The line is written only when the money is exactly what the booking asked for; anything else is left for a person.
/// </summary>
public static class OnlineDepositRules
{
    /// <param name="payment">What the gateway holds.</param>
    /// <param name="due">The deposit the booking asks for (ADR 0023); null when it asks for none.</param>
    /// <param name="recordedReference">The reference on the visit's deposit line; null when it has no reference.</param>
    /// <param name="hasDeposit">Whether the visit already has a deposit line, held or returned.</param>
    public static OnlineDepositOutcome Decide(OnlineDepositPayment payment, Money? due, bool hasDeposit, string? recordedReference)
    {
        ArgumentNullException.ThrowIfNull(payment);

        if (!IsReference(payment.Reference) || string.IsNullOrWhiteSpace(payment.ProviderSystemName))
        {
            return OnlineDepositOutcome.ReferenceUnusable;
        }

        if (hasDeposit)
        {
            // One deposit per visit (ADR 0023). The same session again is the line that is already there.
            return string.Equals(recordedReference, payment.Reference, StringComparison.Ordinal)
                ? OnlineDepositOutcome.AlreadyRecorded
                : OnlineDepositOutcome.AnotherDepositRecorded;
        }

        if (due is not { } asked)
        {
            return OnlineDepositOutcome.NoDepositAsked;
        }

        if (!string.Equals(asked.Currency, payment.Amount.Currency, StringComparison.Ordinal))
        {
            return OnlineDepositOutcome.CurrencyMismatch;
        }

        return asked.Amount == payment.Amount.Amount ? OnlineDepositOutcome.Recorded : OnlineDepositOutcome.AmountMismatch;
    }

    /// <summary>A reference the line can carry whole: cutting one short would make two sessions look like one.</summary>
    public static bool IsReference(string? reference)
        => !string.IsNullOrWhiteSpace(reference) && reference.Length <= ClientAccountEntry.ProviderReferenceMaxLength;
}

public enum OnlineDepositOutcome
{
    /// <summary>The deposit is on the ledger as held, written now.</summary>
    Recorded = 1,

    /// <summary>This session's deposit was already on the ledger; nothing was written.</summary>
    AlreadyRecorded = 2,

    /// <summary>The salon has no such visit.</summary>
    NotFound = 3,

    /// <summary>The booking asks for no deposit, so none is recorded for it.</summary>
    NoDepositAsked = 4,

    /// <summary>The visit already has a deposit from somewhere else (the desk, another session): this money needs a person.</summary>
    AnotherDepositRecorded = 5,

    /// <summary>The money is in another currency than the booking.</summary>
    CurrencyMismatch = 6,

    /// <summary>The money is not the amount the booking asks for.</summary>
    AmountMismatch = 7,

    /// <summary>The gateway's reference is empty or too long to keep whole.</summary>
    ReferenceUnusable = 8,

    /// <summary>Another request is writing this visit's deposit at this very moment; it will be there when it finishes.</summary>
    BeingRecorded = 9,
}