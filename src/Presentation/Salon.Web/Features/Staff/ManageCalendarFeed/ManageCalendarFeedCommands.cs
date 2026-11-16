using Microsoft.EntityFrameworkCore;
using Salon.Core.Cqrs;
using Salon.Core.Staff;
using Salon.Data;

namespace Salon.Web.Features.Staff.ManageCalendarFeed;

/// <summary>A staff member and whether their calendar address is on (ADR 0038). Null when the salon has no such person.</summary>
public sealed record CalendarFeedStateQuery(Guid StaffMemberId) : IQuery<CalendarFeedState?>;

/// <param name="Token">The secret of the address; null while it is switched off.</param>
public sealed record CalendarFeedState(Guid StaffMemberId, string DisplayName, bool IsActive, string? Token)
{
    public bool IsOn => Token is not null;
}

/// <summary>
/// Gives the staff member a new calendar address (ADR 0038): switches it on, or replaces the address it had, which
/// stops working at once. False when the salon has no such person.
/// </summary>
public sealed record NewCalendarFeedAddressCommand(Guid StaffMemberId) : ICommand<bool>;

/// <summary>Switches the staff member's calendar address off; it stops working at once. False when the salon has no such person.</summary>
public sealed record SwitchCalendarFeedOffCommand(Guid StaffMemberId) : ICommand<bool>;

/// <summary>Reads through the tenant filter, so another salon's id finds nothing.</summary>
public sealed class CalendarFeedStateHandler(SalonDbContext db) : IRequestHandler<CalendarFeedStateQuery, CalendarFeedState?>
{
    public async Task<CalendarFeedState?> HandleAsync(CalendarFeedStateQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await db.StaffMembers.AsNoTracking()
            .Where(s => s.Id == request.StaffMemberId)
            .Select(s => new CalendarFeedState(s.Id, s.DisplayName, s.IsActive, s.CalendarFeedToken))
            .FirstOrDefaultAsync(cancellationToken);
    }
}

public sealed class NewCalendarFeedAddressHandler(SalonDbContext db) : IRequestHandler<NewCalendarFeedAddressCommand, bool>
{
    public async Task<bool> HandleAsync(NewCalendarFeedAddressCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var person = await db.StaffMembers.FirstOrDefaultAsync(s => s.Id == request.StaffMemberId, cancellationToken);
        if (person is null)
        {
            return false;
        }

        person.CalendarFeedToken = CalendarFeedToken.Generate();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed class SwitchCalendarFeedOffHandler(SalonDbContext db) : IRequestHandler<SwitchCalendarFeedOffCommand, bool>
{
    public async Task<bool> HandleAsync(SwitchCalendarFeedOffCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var person = await db.StaffMembers.FirstOrDefaultAsync(s => s.Id == request.StaffMemberId, cancellationToken);
        if (person is null)
        {
            return false;
        }

        person.CalendarFeedToken = null;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}