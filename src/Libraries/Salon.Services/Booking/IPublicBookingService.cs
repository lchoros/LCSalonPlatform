using NodaTime;

using Salon.Core.Appointments;

namespace Salon.Services.Booking;

/// <summary>
/// The client-facing booking flow (ADR 0014) for the current tenant: start a booking (which sends an SMS code when
/// <see cref="Core.Settings.BookingSettings.RequireSmsVerification"/> is on, or books at once when it is off), verify the
/// code and book, show a confirmation, and cancel by the secret link. Clients are found or created by normalised phone
/// through the client service; appointments go through <see cref="Appointments.IBookingService"/> with
/// <see cref="AppointmentSource.Online"/>. Called from commands, everything shares the pipeline's transaction (ADR 0003).
/// </summary>
public interface IPublicBookingService
{
    /// <summary>
    /// Validates the request against the salon (service active, phone normalisable with the salon's country), then either
    /// stores a <see cref="Core.Booking.PhoneVerification"/> carrying the booking and sends the code, or — with SMS
    /// verification off — books immediately. Throws <see cref="ArgumentException"/> for bad input,
    /// <see cref="PhoneVerificationRateLimitedException"/> after <see cref="Core.Booking.PhoneVerificationRules.MaxCodesPerPhone"/>
    /// codes to one number in an hour, and <see cref="Appointments.SlotNoLongerAvailableException"/> on the immediate path.
    /// </summary>
    Task<PublicBookingStart> StartAsync(PublicBookingRequest request, CancellationToken cancellationToken = default);

    /// <summary>What the verify page shows: the masked phone and whether the row is still usable. Null when the id is not this salon's.</summary>
    Task<PendingVerification?> GetVerificationAsync(Guid verificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the code and, when it is right, finds or creates the client and books the appointment the row carries. A
    /// wrong code counts an attempt and is persisted; the right code on a row that already booked returns that booking.
    /// Never throws for a client mistake: the outcome says what happened.
    /// </summary>
    Task<PublicBookingVerification> VerifyAndBookAsync(Guid verificationId, string? code, CancellationToken cancellationToken = default);

    /// <summary>The confirmation of an online booking; null for an unknown id or an appointment made in the admin.</summary>
    Task<PublicAppointment?> FindAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>The appointment behind a cancel link; null for an unknown or malformed token.</summary>
    Task<PublicAppointment?> FindByTokenAsync(string? publicToken, CancellationToken cancellationToken = default);

    /// <summary>Cancels as <see cref="AppointmentStatus.CancelledByClient"/> when the token is known, the appointment is still active and has not started.</summary>
    Task<PublicCancellation> CancelByTokenAsync(string? publicToken, CancellationToken cancellationToken = default);
}

/// <summary>What the details form collects, plus the slot chosen one step earlier. Text is trimmed and normalised by the service.</summary>
/// <param name="StaffMemberId">Null for "anyone available"; the person is chosen when the booking is made.</param>
/// <param name="ConsentWording">The exact consent text shown; required when <paramref name="MarketingConsent"/> is true.</param>
public sealed record PublicBookingRequest(
    Guid ServiceId,
    Guid? StaffMemberId,
    Instant Start,
    string FirstName,
    string LastName,
    string Phone,
    string? Email,
    bool MarketingConsent,
    string? ConsentWording)
{
    /// <summary>Keeps the phone and email out of any log line that prints the request.</summary>
    public override string ToString() => $"{nameof(PublicBookingRequest)} {{ ServiceId = {ServiceId}, Start = {Start} }}";
}

/// <summary>Either a verification to complete (<see cref="VerificationId"/>) or, with SMS verification off, the booking itself.</summary>
public sealed record PublicBookingStart(Guid? VerificationId, Guid? AppointmentId)
{
    public bool RequiresVerification => VerificationId is not null;
}

/// <param name="PhoneE164">The number the code went to; the page masks it.</param>
/// <param name="AppointmentId">Set once the row has booked; the page then goes straight to the confirmation.</param>
public sealed record PendingVerification(Guid Id, string PhoneE164, Guid ServiceId, Guid? AppointmentId, bool IsUsable, int AttemptsLeft);

public enum PublicBookingVerificationOutcome
{
    Booked = 1,
    WrongCode = 2,
    Expired = 3,
    TooManyAttempts = 4,
    SlotTaken = 5,
    NotFound = 6,
}

/// <param name="ServiceId">For the "pick another time" link when the slot is gone or the row is dead.</param>
public sealed record PublicBookingVerification(PublicBookingVerificationOutcome Outcome, Guid? AppointmentId = null, int AttemptsLeft = 0, Guid? ServiceId = null);

/// <summary>What a client may see about their own booking: no names of other people, no price breakdown, no notes.</summary>
public sealed record PublicAppointment(
    Guid Id,
    Guid ServiceId,
    string ServiceName,
    string StaffName,
    Instant Start,
    Instant End,
    AppointmentStatus Status,
    string PublicToken)
{
    public bool IsActive => AppointmentStateMachine.IsActive(Status);
}

public enum PublicCancellationOutcome
{
    Cancelled = 1,
    NotFound = 2,
    AlreadyCancelled = 3,
    TooLate = 4,
}

public sealed record PublicCancellation(PublicCancellationOutcome Outcome, PublicAppointment? Appointment = null);
