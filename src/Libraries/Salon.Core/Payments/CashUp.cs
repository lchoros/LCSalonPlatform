using NodaTime;

using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Payments;

/// <summary>
/// The end-of-day count of the cash drawer (ADR 0024, glossary: "Raport kasowy"). Tenant-owned, one per salon and day,
/// written once: what the lines of that day said the drawer should hold at that moment, what the person counted, and a
/// remark for the difference. It records a count; it never changes a payment.
/// </summary>
public class CashUp : BaseEntity, ITenantEntity
{
    public const int NoteMaxLength = 500;
    public const int CurrencyLength = 3;

    public Guid TenantId { get; set; }

    /// <summary>The salon's local day the count closes.</summary>
    public LocalDate Day { get; set; }

    /// <summary>Cash in less cash out over the day's lines when the day was closed.</summary>
    public decimal ExpectedCash { get; set; }

    /// <summary>What was in the drawer from the day's takings, as counted.</summary>
    public decimal CountedCash { get; set; }

    /// <summary>ISO 4217 code of both amounts.</summary>
    public required string Currency { get; set; }

    public string? Note { get; set; }

    public Instant ClosedAt { get; set; }

    /// <summary>The login that closed the day.</summary>
    public Guid? ClosedByUserId { get; set; }

    /// <summary>Counted less expected: above zero is money over, below is money short.</summary>
    public decimal Difference => CountedCash - ExpectedCash;
}