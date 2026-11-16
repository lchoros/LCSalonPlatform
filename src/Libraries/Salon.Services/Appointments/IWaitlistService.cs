using NodaTime;
using Salon.Core.Appointments;

namespace Salon.Services.Appointments;

/// <summary>
/// The salon's waitlist (ADR 0041): clients who want a visit the diary has no room for yet. A service, not a slice,
/// because the list page, the form, the count on the diary pages and the booking event all use it.
/// </summary>
public interface IWaitlistService
{
    /// <summary>How many clients are waiting today.</summary>
    Task<int> CountWaitingAsync(CancellationToken cancellationToken = default);

    /// <summary>The waiting clients, the one who asked first on top, with the names the desk reads.</summary>
    Task<IReadOnlyList<WaitlistItem>> ListWaitingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the client on the list for the service. A client already waiting for that service keeps their place and
    /// gets the new days, person and remark. The outcome says why nothing was written when it is not
    /// <see cref="WaitlistAddOutcome.Added"/> or <see cref="WaitlistAddOutcome.Updated"/>.
    /// </summary>
    Task<WaitlistAddOutcome> AddAsync(WaitlistRequest request, Guid? byUserId, CancellationToken cancellationToken = default);

    /// <summary>Takes a waiting client off the list by hand. False when the salon has no such waiting entry.</summary>
    Task<bool> RemoveAsync(Guid entryId, Guid? byUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes what a booking fulfils: the client's waiting entries for the booked service. Returns how many were closed.
    /// </summary>
    Task<int> CloseForBookingAsync(Guid clientId, Guid serviceId, Guid appointmentId, CancellationToken cancellationToken = default);
}

/// <param name="StaffMemberId">The person the client wants; null when anyone will do.</param>
public sealed record WaitlistRequest(Guid ClientId, Guid ServiceId, Guid? StaffMemberId, LocalDate? NotBefore, LocalDate? NotAfter, string? Note);

public enum WaitlistAddOutcome
{
    Added,

    /// <summary>The client was already waiting for this service; the entry kept its place and took the new details.</summary>
    Updated,

    /// <summary>The salon has no such client, or the card is inactive.</summary>
    ClientUnknown,

    /// <summary>The salon has no such service in use.</summary>
    ServiceUnknown,

    /// <summary>The salon has no such person taking bookings.</summary>
    StaffUnknown,

    /// <summary>The last day is before the first, or already past.</summary>
    DaysInvalid,

    /// <summary>The remark is too long.</summary>
    NoteTooLong,
}

/// <summary>One waiting client as the desk reads it.</summary>
/// <param name="StaffName">Null when anyone will do.</param>
/// <param name="BookingDay">The day the booking form opens on for this client: the first day they can come, never in the past.</param>
/// <param name="WaitingSince">In the salon's zone.</param>
public sealed record WaitlistItem(
    Guid Id,
    Guid ClientId,
    string ClientName,
    string? ClientPhone,
    Guid ServiceId,
    string ServiceName,
    Guid? StaffMemberId,
    string? StaffName,
    LocalDate? NotBefore,
    LocalDate? NotAfter,
    string? Note,
    LocalDate BookingDay,
    LocalDate WaitingSince);