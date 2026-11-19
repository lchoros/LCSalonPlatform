namespace Salon.Core.Appointments;

/// <summary>Who made the booking: the client on the public page, a staff member in the admin, or the salon's old system (ADR 0067).</summary>
public enum AppointmentSource
{
    Online = 1,
    Admin = 2,

    /// <summary>Brought over from the diary of the salon's old system (ADR 0067): already confirmed there, so not confirmed again.</summary>
    Import = 3,
}