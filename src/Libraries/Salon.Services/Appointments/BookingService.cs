using Microsoft.EntityFrameworkCore;

using NodaTime;

using Npgsql;

using Salon.Core.Appointments;
using Salon.Core.Appointments.Events;
using Salon.Core.Booking;
using Salon.Core.Events;
using Salon.Core.Payments;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Availability;
using Salon.Services.Catalog;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Services.Appointments;

internal sealed class BookingService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IAvailabilityService availability,
    IServiceOfferingResolver offerings,
    ISettingService settings,
    IEventPublisher publisher,
    IClock clock) : IBookingService
{
    public async Task<Appointment> BookAsync(BookAppointmentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Notes?.Length > Appointment.NotesMaxLength)
        {
            throw new ArgumentException($"Notes may have at most {Appointment.NotesMaxLength} characters.", nameof(request));
        }

        var zone = await TenantZone.ResolveAsync(tenantContext, tenantLookup, cancellationToken).ConfigureAwait(false);
        var service = await db.Services.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId && s.IsActive, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Service {request.ServiceId} does not exist or is not active in this salon.");
        var staffExists = await db.StaffMembers
            .AnyAsync(s => s.Id == request.StaffMemberId && s.IsActive && s.IsBookable, cancellationToken)
            .ConfigureAwait(false);
        if (!staffExists)
        {
            throw new InvalidOperationException($"Staff member {request.StaffMemberId} does not exist or is not bookable in this salon.");
        }

        // This person's own duration and price, or the service's (ADR 0018); someone who does not offer it cannot be booked.
        var offering = await offerings.ResolveAsync(request.ServiceId, request.StaffMemberId, cancellationToken).ConfigureAwait(false);
        if (!offering.Offers)
        {
            throw new InvalidOperationException($"Staff member {request.StaffMemberId} does not offer service {request.ServiceId}.");
        }

        // The same rules the client saw: working hours, other bookings, notice and advance limit, as of now.
        var day = request.Start.InZone(zone).Date;
        var slots = await availability.GetSlotsAsync(request.ServiceId, request.StaffMemberId, day, day, cancellationToken).ConfigureAwait(false);
        if (!slots.Any(s => s.Start == request.Start))
        {
            throw new SlotNoLongerAvailableException();
        }

        // The deposit this booking asks for (ADR 0023): the client's own rule, else the service's, else the salon's.
        var bookingRules = await settings.LoadAsync<BookingSettings>(cancellationToken).ConfigureAwait(false);
        var clientDepositPercent = await db.Clients.AsNoTracking()
            .Where(c => c.Id == request.ClientId)
            .Select(c => c.DepositPercent)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var depositPercent = DepositRules.Percent(bookingRules.DefaultDepositPercent, service.DepositPercent, clientDepositPercent);

        var now = clock.GetCurrentInstant();
        var appointment = new Appointment
        {
            TenantId = tenantContext.TenantId!.Value,
            ClientId = request.ClientId,
            StaffMemberId = request.StaffMemberId,
            ServiceId = request.ServiceId,
            Start = request.Start,
            End = request.Start.Plus(offering.Duration),
            // Online bookings get the secret of their cancel link (ADR 0014); admin bookings have no public page yet.
            PublicToken = request.Source == AppointmentSource.Online ? PublicBookingToken.Generate() : null,
            Status = AppointmentStatus.Booked,
            Source = request.Source,
            Notes = request.Notes,
            PriceAmount = offering.Price,
            DepositAmount = DepositRules.Amount(offering.Price, depositPercent),
            Currency = service.Currency,
            DurationMinutes = (int)offering.Duration.TotalMinutes,
            CreatedAt = now,
            CreatedByUserId = request.ByUserId,
        };
        var change = new AppointmentStatusChange
        {
            TenantId = appointment.TenantId,
            AppointmentId = appointment.Id,
            From = null,
            To = AppointmentStatus.Booked,
            At = now,
            ByUserId = request.ByUserId,
        };

        db.Appointments.Add(appointment);
        db.AppointmentStatusChanges.Add(change);
        try
        {
            // The publisher's SaveChangesAsync flushes the appointment, its status change and the outbox row in one batch
            // (ADR 0007), so even outside a command transaction they commit together, and the exclusion constraint fires here.
            await publisher.PublishAsync(
                new AppointmentBooked(appointment.Id, appointment.TenantId, appointment.ClientId, appointment.StaffMemberId, appointment.ServiceId, appointment.Start, appointment.End, appointment.Source),
                cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
        {
            // Another booking of the same person won the race between our slot check and the insert. Forget the rows we
            // added (appointment, status change, outbox message) so the scope stays usable for a fresh slot list.
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }

            throw new SlotNoLongerAvailableException("The chosen time has just been taken; please pick another slot.", ex);
        }

        return appointment;
    }

    public Task<Appointment> ConfirmAsync(Guid appointmentId, Guid? byUserId = null, CancellationToken cancellationToken = default)
        => TransitionAsync(appointmentId, AppointmentStatus.Confirmed, byUserId, null, (a, at) => new AppointmentConfirmed(a.Id, a.TenantId, at), cancellationToken);

    public Task<Appointment> MarkArrivedAsync(Guid appointmentId, Guid? byUserId = null, CancellationToken cancellationToken = default)
        => TransitionAsync(appointmentId, AppointmentStatus.Arrived, byUserId, null, (a, at) => new AppointmentArrived(a.Id, a.TenantId, at), cancellationToken);

    public Task<Appointment> CompleteAsync(Guid appointmentId, Guid? byUserId = null, CancellationToken cancellationToken = default)
        => TransitionAsync(appointmentId, AppointmentStatus.Completed, byUserId, null, (a, at) => new AppointmentCompleted(a.Id, a.TenantId, at), cancellationToken);

    public Task<Appointment> MarkNoShowAsync(Guid appointmentId, Guid? byUserId = null, CancellationToken cancellationToken = default)
        => TransitionAsync(appointmentId, AppointmentStatus.NoShow, byUserId, null, (a, at) => new AppointmentNoShow(a.Id, a.TenantId, at), cancellationToken);

    public Task<Appointment> CancelAsync(Guid appointmentId, bool byClient, string? reason, Guid? byUserId = null, CancellationToken cancellationToken = default)
    {
        if (reason?.Length > Appointment.CancellationReasonMaxLength)
        {
            throw new ArgumentException($"A cancellation reason may have at most {Appointment.CancellationReasonMaxLength} characters.", nameof(reason));
        }

        var to = byClient ? AppointmentStatus.CancelledByClient : AppointmentStatus.CancelledBySalon;
        return TransitionAsync(appointmentId, to, byUserId, reason, (a, at) => new AppointmentCancelled(a.Id, a.TenantId, at, a.Status), cancellationToken);
    }

    public async Task<Appointment> MoveAsync(Guid appointmentId, Guid staffMemberId, Instant newStart, Guid? byUserId = null, CancellationToken cancellationToken = default)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new AppointmentNotFoundException(appointmentId);
        if (!AppointmentStateMachine.IsActive(appointment.Status))
        {
            throw new AppointmentNotActiveException(appointmentId, appointment.Status);
        }

        var staffExists = await db.StaffMembers
            .AnyAsync(s => s.Id == staffMemberId && s.IsActive && s.IsBookable, cancellationToken)
            .ConfigureAwait(false);
        if (!staffExists)
        {
            throw new InvalidOperationException($"Staff member {staffMemberId} does not exist or is not bookable in this salon.");
        }

        // The same rules as a new booking, except that the time this visit holds now is free for it.
        var zone = await TenantZone.ResolveAsync(tenantContext, tenantLookup, cancellationToken).ConfigureAwait(false);
        var day = newStart.InZone(zone).Date;
        var slots = await availability.GetSlotsAsync(appointment.ServiceId, staffMemberId, day, day, appointment.Id, cancellationToken).ConfigureAwait(false);
        if (!slots.Any(s => s.Start == newStart))
        {
            throw new SlotNoLongerAvailableException();
        }

        var now = clock.GetCurrentInstant();
        var oldStart = appointment.Start;
        appointment.Start = newStart;
        appointment.End = newStart.Plus(Duration.FromMinutes(appointment.DurationMinutes));
        appointment.StaffMemberId = staffMemberId;
        db.AppointmentStatusChanges.Add(new AppointmentStatusChange
        {
            TenantId = appointment.TenantId,
            AppointmentId = appointment.Id,
            From = appointment.Status,
            To = appointment.Status,
            At = now,
            ByUserId = byUserId,
            Reason = IBookingService.MoveReason,
        });
        try
        {
            await publisher.PublishAsync(
                new AppointmentMoved(appointment.Id, appointment.TenantId, oldStart, newStart, staffMemberId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
        {
            // Another booking of the new person won the race between the slot check and the update. Put the appointment back
            // as loaded and forget the rows we added, so the scope stays usable for a fresh slot list.
            var entry = db.Entry(appointment);
            entry.CurrentValues.SetValues(entry.OriginalValues);
            entry.State = EntityState.Unchanged;
            foreach (var added in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
            {
                added.State = EntityState.Detached;
            }

            throw new SlotNoLongerAvailableException("The chosen time has just been taken; please pick another slot.", ex);
        }

        return appointment;
    }

    public async Task<IReadOnlyList<Appointment>> GetDayAsync(LocalDate day, CancellationToken cancellationToken = default)
    {
        var zone = await TenantZone.ResolveAsync(tenantContext, tenantLookup, cancellationToken).ConfigureAwait(false);
        var dayStart = zone.AtStartOfDay(day).ToInstant();
        var dayEnd = zone.AtStartOfDay(day.PlusDays(1)).ToInstant();
        var active = AppointmentStateMachine.Active.ToArray();

        return await db.Appointments.AsNoTracking()
            .Where(a => active.Contains(a.Status) && a.Start >= dayStart && a.Start < dayEnd)
            .OrderBy(a => a.Start).ThenBy(a => a.StaffMemberId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Appointment> TransitionAsync(
        Guid appointmentId,
        AppointmentStatus to,
        Guid? byUserId,
        string? reason,
        Func<Appointment, Instant, IEvent> eventFor,
        CancellationToken cancellationToken)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new AppointmentNotFoundException(appointmentId);

        var now = clock.GetCurrentInstant();
        // Throws InvalidAppointmentTransitionException before anything is tracked as changed.
        var change = AppointmentStateMachine.Transition(appointment, to, now, byUserId, reason);
        db.AppointmentStatusChanges.Add(change);
        await publisher.PublishAsync(eventFor(appointment, now), cancellationToken).ConfigureAwait(false);
        return appointment;
    }
}