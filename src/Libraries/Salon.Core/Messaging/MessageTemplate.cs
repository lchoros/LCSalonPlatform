using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Messaging;

/// <summary>
/// One salon's own wording for a template (ADR 0008): the row that overrides the embedded platform default for
/// (<see cref="Key"/>, <see cref="Channel"/>, <see cref="Culture"/>). No row means "use the default".
/// </summary>
public class MessageTemplate : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>A key from <see cref="MessageTemplateCatalog"/>, e.g. "Booking.Confirmation".</summary>
    public required string Key { get; set; }

    public MessageChannel Channel { get; set; }

    /// <summary>One of <see cref="Localization.SupportedCultures.All"/>, e.g. "pl-PL".</summary>
    public required string Culture { get; set; }

    /// <summary>Email only; null for SMS.</summary>
    public string? Subject { get; set; }

    /// <summary>Plain text with <c>{{Token}}</c> placeholders taken from the key's catalogue entry.</summary>
    public required string Body { get; set; }

    /// <summary>An inactive row is ignored and the default applies again, without losing the salon's text.</summary>
    public bool IsActive { get; set; } = true;

    public Instant UpdatedAt { get; set; }
}