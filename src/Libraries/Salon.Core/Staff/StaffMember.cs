using System.Text.RegularExpressions;
using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Staff;

/// <summary>
/// A bookable person in a salon (glossary: "Pracownik / Specjalista"), ADR 0010. Tenant-owned. The optional
/// <see cref="UserId"/> ties the person to a login of the same salon; it is a plain column, not a foreign key, so a
/// staff member can exist before (or without) an account and an account can be removed without touching the rota.
/// Weekly working hours live in <see cref="WorkingHours"/> rows; exceptions, time off and breaks come in the next slice.
/// </summary>
public partial class StaffMember : BaseEntity, ITenantEntity
{
    public const int DisplayNameMaxLength = 100;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 30;
    public const int ColourLength = 7;

    /// <summary>Warm grey (Tailwind stone-500): neutral until the salon picks a colour.</summary>
    public const string DefaultColour = "#78716C";

    public Guid TenantId { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>Stored lower-case; optional, used later for notifications to the staff member.</summary>
    public string? Email { get; set; }

    public string? Phone { get; set; }

    /// <summary>Login of the same salon this person uses, if any. No FK on purpose (see class remarks).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Hex <c>#RRGGBB</c>, upper-case; the calendar colours this person's appointments with it.</summary>
    public string Colour { get; set; } = DefaultColour;

    /// <summary>Inactive staff keep their history but disappear from pickers and the public booking page.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>False for people who work in the salon but take no appointments themselves (an owner, a trainee).</summary>
    public bool IsBookable { get; set; } = true;

    public int SortOrder { get; set; }

    public Instant CreatedAt { get; set; }

    /// <summary>True for <c>#RRGGBB</c> in either case; the entity stores the upper-case form.</summary>
    public static bool IsValidColour(string? colour) => colour is not null && ColourPattern().IsMatch(colour);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColourPattern();
}