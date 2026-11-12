using Salon.Core.Cqrs;
using Salon.Core.Staff;

namespace Salon.Web.Features.Staff.EditStaff;

/// <summary>
/// Updates an existing staff member in the current salon. Bound straight from the admin form;
/// <see cref="EditStaffValidator"/> holds the rules, the handler trims the text, lower-cases the email and upper-cases the colour.
/// </summary>
/// <param name="Id">The unique identifier of the staff member.</param>
/// <param name="DisplayName">Shown to clients when they choose who to book with.</param>
/// <param name="Email">Optional, for notifications to the staff member later.</param>
/// <param name="Phone">Optional.</param>
/// <param name="Colour">Hex <c>#RRGGBB</c> as posted by <c>&lt;input type="color"&gt;</c>.</param>
/// <param name="IsBookable">False for people who take no appointments themselves.</param>
/// <param name="IsActive">False for people who are not active.</param>
public sealed record EditStaffCommand(Guid Id, string DisplayName, string? Email, string? Phone, string Colour, bool IsBookable, bool IsActive) : ICommand<bool>
{
}