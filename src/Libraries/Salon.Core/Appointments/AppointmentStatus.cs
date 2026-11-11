namespace Salon.Core.Appointments;

/// <summary>
/// Life cycle of an appointment (ADR 0012). The numeric values are part of the schema: the exclusion constraint on
/// <c>appointments</c> only guards rows with status 1, 2 or 3, so cancelled and finished rows free the time.
/// </summary>
public enum AppointmentStatus
{
    Booked = 1,
    Confirmed = 2,
    Arrived = 3,
    Completed = 4,
    NoShow = 5,
    CancelledByClient = 6,
    CancelledBySalon = 7,
}
