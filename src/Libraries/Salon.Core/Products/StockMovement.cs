using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Products;

/// <summary>
/// One line of a product's stock history (ADR 0036): units that came in, were used up, were written off, or a count
/// that put the number right. Tenant-owned and append-only, like a line on a client's account: a mistake is corrected
/// by another line, never by editing one, so the list says who changed the shelf, when and why.
/// <see cref="Quantity"/> is signed: above zero adds to the shelf, below zero takes from it.
/// </summary>
public class StockMovement : BaseEntity, ITenantEntity
{
    public const int NoteMaxLength = 200;

    public Guid TenantId { get; set; }

    public Guid ProductId { get; set; }

    public StockMovementKind Kind { get; set; }

    /// <summary>Units added (above zero) or taken (below zero); never zero.</summary>
    public int Quantity { get; set; }

    /// <summary>What the shelf held according to the lines once this one was added.</summary>
    public int OnHandAfter { get; set; }

    /// <summary>Why something was written off; an optional remark otherwise (a supplier, an invoice number).</summary>
    public string? Note { get; set; }

    /// <summary>
    /// The visit on whose bill the units were sold (ADR 0037). Also set on the count that a sale forces when the record
    /// held fewer than the client took home: the bottle in their hand is the count.
    /// </summary>
    public Guid? AppointmentId { get; set; }

    public Instant At { get; set; }

    /// <summary>The login that added the line.</summary>
    public Guid? ByUserId { get; set; }
}

public enum StockMovementKind
{
    /// <summary>A delivery arrived; adds to the shelf.</summary>
    Received = 1,

    /// <summary>Used up at the chair or the basin; takes from the shelf.</summary>
    UsedInSalon = 2,

    /// <summary>Broken, out of date or lost; takes from the shelf, with the reason.</summary>
    WrittenOff = 3,

    /// <summary>Somebody counted the shelf and the number differed; the line is the difference, either way.</summary>
    Counted = 4,

    /// <summary>Sold to a client on a visit's bill (ADR 0037); takes from the shelf when the bill is paid.</summary>
    Sold = 5,

    /// <summary>A client brought it back and it went on the shelf again (ADR 0040); adds to the shelf, with the visit it was sold on.</summary>
    Returned = 6,
}