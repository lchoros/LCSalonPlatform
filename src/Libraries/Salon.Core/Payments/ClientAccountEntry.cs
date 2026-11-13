using NodaTime;

using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Payments;

/// <summary>
/// One line of a client's account with the salon (ADR 0022, ADR 0023): a fee the client owes or what settled it, a
/// deposit the salon received or gave back. Tenant-owned and
/// append-only: a line is never edited or deleted, a mistake is corrected by another line (a fee is waived, not removed),
/// so the list itself is the audit trail of who decided what and when. <see cref="Amount"/> is always positive; whether
/// it is owed or settles a debt follows from <see cref="Kind"/> (<see cref="ClientAccountRules"/>).
/// Nothing here charges anybody: a payment line records money the salon took itself (ADR 0022, Phase 1).
/// </summary>
public class ClientAccountEntry : BaseEntity, ITenantEntity
{
    public const int NoteMaxLength = 500;
    public const int CurrencyLength = 3;
    public const int ProviderNameMaxLength = 100;
    public const int ProviderReferenceMaxLength = 200;

    public Guid TenantId { get; set; }

    public Guid ClientId { get; set; }

    /// <summary>The visit the line is about; every fee and its settlement carry it.</summary>
    public Guid? AppointmentId { get; set; }

    public ClientAccountEntryKind Kind { get; set; }

    /// <summary>Always above zero; the direction is the kind's.</summary>
    public decimal Amount { get; set; }

    /// <summary>ISO 4217 code of <see cref="Amount"/>.</summary>
    public required string Currency { get; set; }

    /// <summary>How the money changed hands; on payment, deposit and return lines.</summary>
    public PaymentMethod? Method { get; set; }

    /// <summary>System name of the payment plugin that recorded a payment line.</summary>
    public string? ProviderSystemName { get; set; }

    /// <summary>The plugin's reference for a payment line. Never card data.</summary>
    public string? ProviderReference { get; set; }

    /// <summary>The fee a waiver or payment settles, or the deposit a return gives back.</summary>
    public Guid? RelatedEntryId { get; set; }

    /// <summary>Why a fee was waived; optional remark otherwise.</summary>
    public string? Note { get; set; }

    public Instant At { get; set; }

    /// <summary>The login that added the line.</summary>
    public Guid? ByUserId { get; set; }

    public Money Money => new(Amount, Currency);
}

public enum ClientAccountEntryKind
{
    /// <summary>The client did not turn up; they owe this.</summary>
    NoShowFee = 1,

    /// <summary>The visit was cancelled inside the notice period; they owe this.</summary>
    LateCancellationFee = 2,

    /// <summary>The salon let a fee go; settles it in full.</summary>
    FeeWaived = 3,

    /// <summary>The client paid a fee at the salon; settles it in full.</summary>
    FeePayment = 4,

    /// <summary>The salon received a deposit for a visit (ADR 0023).</summary>
    DepositPaid = 5,

    /// <summary>The salon gave a deposit back, in full.</summary>
    DepositReturned = 6,

    /// <summary>The client paid for a visit at the checkout (ADR 0024): the price less the deposit the salon held.</summary>
    VisitPayment = 7,
}