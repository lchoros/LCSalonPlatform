using Salon.Core.Appointments;

namespace Salon.Services.Appointments;

/// <summary>Only booked, confirmed or arrived appointments hold a slot, so only they can be moved (ADR 0016).</summary>
public class AppointmentNotActiveException : InvalidOperationException
{
    public AppointmentNotActiveException()
    {
    }

    public AppointmentNotActiveException(string message)
        : base(message)
    {
    }

    public AppointmentNotActiveException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AppointmentNotActiveException(Guid appointmentId, AppointmentStatus status)
        : base($"Appointment {appointmentId} has status {status} and cannot be moved.")
    {
        AppointmentId = appointmentId;
        Status = status;
    }

    public Guid? AppointmentId { get; }

    public AppointmentStatus? Status { get; }
}