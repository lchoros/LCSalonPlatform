using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Availability;
using Salon.Services.Payments;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Appointments.Calendar;

/// <summary>
/// Reads through the tenant filter: the appointment, its client's name (never health notes, ADR 0011), service and staff,
/// then asks the availability engine for the free times of the chosen person and day with this appointment not counted
/// as busy (ADR 0016). A service made inactive meanwhile simply yields no times rather than failing the card.
/// </summary>
public sealed class AppointmentCardHandler(
    SalonDbContext db,
    IAvailabilityService availability,
    IClientAccountService accounts,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClock clock) : IRequestHandler<AppointmentCardQuery, AppointmentCard>
{
    public async Task<AppointmentCard> HandleAsync(AppointmentCardQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(AppointmentCardQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb[tenant.TimeZoneId];

        var appointment = await db.Appointments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AppointmentId, cancellationToken)
            ?? throw new AppointmentNotFoundException(request.AppointmentId);

        var clientName = await db.Clients.AsNoTracking()
            .Where(c => c.Id == appointment.ClientId)
            .Select(c => c.FirstName + " " + c.LastName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        var serviceName = await db.Services.AsNoTracking()
            .Where(s => s.Id == appointment.ServiceId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        var staff = await db.StaffMembers.AsNoTracking()
            .Where(s => (s.IsActive && s.IsBookable) || s.Id == appointment.StaffMemberId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName)
            .Select(s => new CalendarStaff(s.Id, s.DisplayName, s.Colour))
            .ToListAsync(cancellationToken);
        var own = staff.FirstOrDefault(s => s.Id == appointment.StaffMemberId);

        var start = appointment.Start.InZone(zone);
        var isActive = AppointmentStateMachine.IsActive(appointment.Status);
        var moveStaffId = request.MoveStaffId is { } wanted && staff.Any(s => s.Id == wanted) ? wanted : appointment.StaffMemberId;
        var moveDate = request.MoveDate ?? start.Date;

        IReadOnlyList<MoveSlot> slots = [];
        if (isActive)
        {
            try
            {
                slots = (await availability.GetSlotsAsync(appointment.ServiceId, moveStaffId, moveDate, moveDate, appointment.Id, cancellationToken))
                    .Where(s => !(s.StaffMemberId == appointment.StaffMemberId && s.Start == appointment.Start))
                    .Select(s => new MoveSlot(s.Start, s.Start.InZone(zone).TimeOfDay))
                    .ToArray();
            }
            catch (InvalidOperationException)
            {
                // The service is no longer active: the visit stays as it is and offers no times.
            }
        }

        var deposit = await accounts.GetDepositAsync(appointment.Id, cancellationToken);

        return new AppointmentCard(
            appointment.Id,
            start.Date,
            start.TimeOfDay,
            appointment.End.InZone(zone).TimeOfDay,
            clientName,
            serviceName,
            own?.DisplayName ?? string.Empty,
            appointment.StaffMemberId,
            own?.Colour ?? StaffMember.DefaultColour,
            appointment.Status,
            appointment.Price,
            appointment.Notes,
            CardActions.For(appointment.Status),
            isActive,
            staff,
            moveStaffId,
            moveDate,
            slots,
            appointment.ClientId,
            (await accounts.GetAccountAsync(appointment.ClientId, cancellationToken)).Owed,
            deposit?.Due,
            deposit?.Deposit is { IsHeld: true } held ? held.Amount : null)
        {
            Today = clock.GetCurrentInstant().InZone(zone).Date,
            Chosen = request.MoveStart is { } tapped ? slots.FirstOrDefault(s => s.Start == tapped) : null,
            History = await HistoryAsync(appointment, staff, zone, cancellationToken),
        };
    }

    /// <summary>
    /// The rows the diary keeps for every booking, move and change of status (ADR 0012), oldest first, read as lines a
    /// person can follow (ADR 0091): what happened, when in the salon's time, and which login did it — nobody named when
    /// the client did it online or a job did.
    /// </summary>
    private async Task<IReadOnlyList<VisitHistoryLine>> HistoryAsync(Appointment appointment, List<CalendarStaff> staff, DateTimeZone zone, CancellationToken cancellationToken)
    {
        var changes = await db.AppointmentStatusChanges.AsNoTracking()
            .Where(c => c.AppointmentId == appointment.Id)
            .OrderBy(c => c.At).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        var userIds = changes.Where(c => c.ByUserId != null).Select(c => c.ByUserId!.Value).Distinct().ToList();
        var users = userIds.Count == 0
            ? []
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        // A person a visit was moved from may no longer be bookable, so the list of the move form does not always hold them.
        var names = staff.ToDictionary(s => s.Id, s => s.DisplayName);
        var missing = changes
            .SelectMany(c => new[] { c.MovedFromStaffMemberId, c.MovedToStaffMemberId })
            .Where(id => id != null && !names.ContainsKey(id.Value))
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (missing.Count > 0)
        {
            foreach (var person in await db.StaffMembers.AsNoTracking().Where(s => missing.Contains(s.Id)).Select(s => new { s.Id, s.DisplayName }).ToListAsync(cancellationToken))
            {
                names[person.Id] = person.DisplayName;
            }
        }

        return [.. changes.Select(change =>
        {
            var kind = VisitHistoryRules.KindOf(change.From, change.To, appointment.Source);
            return new VisitHistoryLine(
                change.At.InZone(zone).LocalDateTime,
                kind,
                change.ByUserId is { } by ? users.GetValueOrDefault(by) : null,
                VisitHistoryRules.ShowsReason(kind) ? change.Reason : null,
                Place(change.MovedFromStart, change.MovedFromStaffMemberId),
                Place(change.MovedToStart, change.MovedToStaffMemberId));
        })];

        VisitPlace? Place(Instant? start, Guid? staffMemberId)
            => start is { } at ? new VisitPlace(at.InZone(zone).LocalDateTime, staffMemberId is { } id ? names.GetValueOrDefault(id) : null) : null;
    }
}