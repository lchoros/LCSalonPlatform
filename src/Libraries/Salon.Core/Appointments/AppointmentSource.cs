namespace Salon.Core.Appointments;

/// <summary>Who made the booking: the client on the public page or a staff member in the admin.</summary>
public enum AppointmentSource
{
    Online = 1,
    Admin = 2,
}