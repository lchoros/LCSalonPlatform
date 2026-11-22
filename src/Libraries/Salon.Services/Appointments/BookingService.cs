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
        var booked = await BookVisitAsync(
            new BookVisitRequest(request.ClientId, request.StaffMemberId, [request.ServiceId], request.Start, request.Source, request.Notes, request.ByUserId),
            cancellationToken).ConfigureAwait(false);
        return booked[0];
    }

    public async Task<IReadOnlyList<Appointment>> BookVisitAsync(BookVisitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Notes?.Length > Appointment.NotesMaxLength)
        {
            throw new ArgumentException($"Notes may have at most {Appointment.NotesMaxLength} characters.", nameof(request));
        }

        if (request.ServiceIds.Count is 0 or > VisitRules.MaxServices || request.ServiceIds.Distinct().Count() != request.ServiceIds.Count)
        {
            throw new ArgumentException($"A visit has one to {VisitRules.MaxServices} services, each once.", nameof(request));
        }

        var zone = await TenantZone.ResolveAsync(tenantContext, tenantLookup, cancellationToken).ConfigureAwait(false);
        var found = await db.Services.AsNoTracking()
            .Where(s => request.ServiceIds.Contains(s.Id) && s.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var services = request.ServiceIds
            .Select(id => found.FirstOrDefault(s => s.Id == id) ?? throw new InvalidOperationException($"Service {id} does not exist or is not active in this salon."))
            .ToList();
        var staffExists = await db.StaffMembers
            .AnyAsync(s => s.Id == request.StaffMemberId && s.IsActive && s.IsBookable, cancellationToken)
            .ConfigureAwait(false);
        if (!staffExists)
        {
            throw new InvalidOperationException($"Staff member {request.StaffMemberId} does not exist or is not bookable in this salon.");
        }

        // This person's own duration and price, or the service's (ADR 0018); someone who does not offer it cannot be booked.
        var offered = new List<ServiceOffering>();
        foreach (var service in services)
        {
            var offering = await offerings.ResolveAsync(service.Id, request.StaffMemberId, cancellationToken).ConfigureAwait(false);
            if (!offering.Offers)
            {
                throw new InvalidOperationException($"Staff member {request.StaffMemberId} does not offer service {service.Id}.");
            }

            offered.Add(offering);
        }

        // The same rules the client saw: working hours, other bookings, notice and advance limit, as of now. For several
        // services the whole visit has to fit, one service after another (ADR 0095).
        var day = request.Start.InZone(zone).Date;
        var slots = await availability.GetVisitSlotsAsync(request.ServiceIds, request.StaffMemberId, day, day, cancellationToken).ConfigureAwait(false);
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

        var now = clock.GetCurrentInstant();
        Guid? visitGroupId = services.Count > 1 ? Guid.CreateVersion7() : null;
        var appointments = new List<Appointment>();
        var start = request.Start;
        for (var i = 0; i < services.Count; i++)
        {
            var service = services[i];
            var offering = offered[i];
            var depositPercent = DepositRules.Percent(bookingRules.DefaultDepositPercent, service.DepositPercent, clientDepositPercent);
            var appointment = new Appointment
            {
                TenantId = tenantContext.TenantId!.Value,
                ClientId = request.ClientId,
                StaffMemberId = request.StaffMemberId,
                ServiceId = service.Id,
                Start = start,
                End = start.Plus(offering.Duration),
                // Every booking gets the secret of the client's own link (ADR 0014, ADR 0029), so a visit booked at the desk can be
                // confirmed, changed and cancelled by the client like one booked online.
                PublicToken = PublicBookingToken.Generate(),
                VisitGroupId = visitGroupId,
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
            db.Appointments.Add(appointment);
            db.AppointmentStatusChanges.Add(new AppointmentStatusChange
            {
                TenantId = appointment.TenantId,
                AppointmentId = appointment.Id,
                From = null,
                To = AppointmentStatus.Booked,
                At = now,
                ByUserId = request.ByUserId,
            });
            appointments.Add(appointment);
            start = appointment.End;
        }

        try
        {
            // The publisher's SaveChangesAsync flushes the appointments, their status changes and the outbox rows together
            // (ADR 0007), so even outside a command transaction they commit as one, and the exclusion constraint fires
            // here: the first publish writes every appointment of the visit, so it is booked whole or not at all.
            foreach (var appointment in appointments)
            {
                await publisher.PublishAsync(
                    new AppointmentBooked(appointment.Id, appointment.TenantId, appointment.ClientId, appointment.StaffMemberId, appointment.ServiceId, appointment.Start, appointment.End, appointment.Source),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
        {
            // Another booking of the same person won the race between our slot check and the insert. Forget the rows we
            // added (appointments, status changes, outbox messages) so the scope stays usable for a fresh slot list.
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }

            throw new SlotNoLongerAvailableException("The chosen time has just been taken; please pick another slot.", ex);
        }

        return appointments;
    }

    public Task<Appointment> ConfirmAsync(Guid appointmentId, Guid? byUserId = null, CancellationToken cancellationToken = default)
        => TransitionAsync(appointmentId, AppointmentStatus.Confirmed, byUserId, null, (a, at) => new AppointmentConfirmed(a.Id, a.TenantId, at), cancellationToken);

    public Task<Appointment> MarkArrivedAsync(Guid appointmentId, Guid? byUserId = null, CancellationToken cancellationToken = default)
        => TransitionAsync(appointmentId, AppointmentStatus.Arrived, byUserId, null, (a, at) => new AppointmentArrived(a.Id, a.TenantId, at), cancellationToken);

    public Task<Appointment> CompleteAsync(Guid appointmentId, Guid? byUserId = null, CancellationToken cancellationToken = default)
        => TransitionAsync(
            appointmentId,
            AppointmentStatus.Completed,
            byUserId,
            null,
            (a, at) => new AppointmentCompleted(a.Id, a.TenantId, at),
            cancellationToken,
            KeepCommissionRatesAsync);

    /// <summary>
    /// The person's commission rates of today go with the visit, so a later raise does not rewrite this month
    /// (ADR 0059). Set before the event is published, whose save writes the visit.
    /// </summary>
    private async Task KeepCommissionRatesAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        var rates = await db.StaffMembers.AsNoTracking()
            .Where(s => s.Id == appointment.StaffMemberId)
            .Select(s => new { s.ServiceCommissionPercent, s.ProductCommissionPercent })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        appointment.CommissionRatesKept = true;
        appointment.ServiceCommissionPercent = rates?.ServiceCommissionPercent;
        appointment.ProductCommissionPercent = rates?.ProductCommissionPercent;
    }

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
        var oldStaffMemberId = appointment.StaffMemberId;
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
            MovedFromStart = oldStart,
            MovedToStart = newStart,
            MovedFromStaffMemberId = oldStaffMemberId,
            MovedToStaffMemberId = staffMemberId,
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
        CancellationToken cancellationToken,
        Func<Appointment, CancellationToken, Task>? alsoChange = null)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new AppointmentNotFoundException(appointmentId);

        var now = clock.GetCurrentInstant();
        // Throws InvalidAppointmentTransitionException before anything is tracked as changed.
        var change = AppointmentStateMachine.Transition(appointment, to, now, byUserId, reason);
        db.AppointmentStatusChanges.Add(change);
        if (alsoChange is not null)
        {
            await alsoChange(appointment, cancellationToken).ConfigureAwait(false);
        }

        await publisher.PublishAsync(eventFor(appointment, now), cancellationToken).ConfigureAwait(false);
        return appointment;
    }
}