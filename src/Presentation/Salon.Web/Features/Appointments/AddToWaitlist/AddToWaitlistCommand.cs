using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Cqrs;
using Salon.Data;
using Salon.Services.Appointments;

namespace Salon.Web.Features.Appointments.AddToWaitlist;

/// <summary>
/// What the "add to the waitlist" form needs (ADR 0041): the client by name, the services in use and the people who
/// take bookings. Null when the salon has no such active client. A query: no transaction, tenant required.
/// </summary>
public sealed record WaitlistFormQuery(Guid ClientId) : IQuery<WaitlistForm?>;

public sealed record WaitlistForm(Guid ClientId, string ClientName, IReadOnlyList<WaitlistOption> Services, IReadOnlyList<WaitlistOption> Staff);

public sealed record WaitlistOption(Guid Id, string Name);

/// <summary>
/// Puts a client on the waitlist for a service (ADR 0041). A command, so the entry is written in the pipeline's
/// transaction (ADR 0003). The rules are the waitlist service's; the outcome says what the form should show.
/// </summary>
/// <param name="StaffMemberId">The person the client wants; null when anyone will do.</param>
/// <param name="ByUserId">The login that adds; set by the controller, never from the form.</param>
public sealed record AddToWaitlistCommand(Guid ClientId, Guid ServiceId, Guid? StaffMemberId, LocalDate? NotBefore, LocalDate? NotAfter, string? Note, Guid? ByUserId) : ICommand<WaitlistAddOutcome>;

/// <summary>Reads through the tenant filter: names only, never health notes (ADR 0011).</summary>
public sealed class WaitlistFormHandler(SalonDbContext db) : IRequestHandler<WaitlistFormQuery, WaitlistForm?>
{
    public async Task<WaitlistForm?> HandleAsync(WaitlistFormQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await db.Clients.AsNoTracking()
            .Where(c => c.Id == request.ClientId && c.IsActive)
            .Select(c => new { c.Id, c.FirstName, c.LastName })
            .FirstOrDefaultAsync(cancellationToken);
        if (client is null)
        {
            return null;
        }

        var services = await db.Services.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s => new WaitlistOption(s.Id, s.Name))
            .ToListAsync(cancellationToken);
        var staff = await db.StaffMembers.AsNoTracking()
            .Where(s => s.IsActive && s.IsBookable)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName)
            .Select(s => new WaitlistOption(s.Id, s.DisplayName))
            .ToListAsync(cancellationToken);
        return new WaitlistForm(client.Id, $"{client.FirstName} {client.LastName}", services, staff);
    }
}

public sealed class AddToWaitlistHandler(IWaitlistService waitlist) : IRequestHandler<AddToWaitlistCommand, WaitlistAddOutcome>
{
    public Task<WaitlistAddOutcome> HandleAsync(AddToWaitlistCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return waitlist.AddAsync(
            new WaitlistRequest(request.ClientId, request.ServiceId, request.StaffMemberId, request.NotBefore, request.NotAfter, request.Note),
            request.ByUserId,
            cancellationToken);
    }
}