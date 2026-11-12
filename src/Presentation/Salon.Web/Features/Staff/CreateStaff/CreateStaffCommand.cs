using Salon.Core.Cqrs;
using Salon.Core.Staff;

namespace Salon.Web.Features.Staff.CreateStaff;

/// <summary>
/// Adds one staff member to the current salon and returns the id. Bound straight from the admin form;
/// <see cref="CreateStaffValidator"/> holds the rules, the handler trims the text, lower-cases the email and upper-cases the colour.
/// </summary>
/// <param name="DisplayName">Shown to clients when they choose who to book with.</param>
/// <param name="Email">Optional, for notifications to the staff member later.</param>
/// <param name="Phone">Optional.</param>
/// <param name="Colour">Hex <c>#RRGGBB</c> as posted by <c>&lt;input type="color"&gt;</c>.</param>
/// <param name="IsBookable">False for people who take no appointments themselves.</param>
public sealed record CreateStaffCommand(string DisplayName, string? Email, string? Phone, string Colour, bool IsBookable) : ICommand<Guid>
{
    /// <summary>The empty form: default colour, bookable.</summary>
    public static CreateStaffCommand Empty { get; } = new(string.Empty, null, null, StaffMember.DefaultColour, true);
}