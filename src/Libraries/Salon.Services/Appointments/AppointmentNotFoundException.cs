namespace Salon.Services.Appointments;

/// <summary>No appointment with that id in the current tenant (another salon's id looks the same as a missing one).</summary>
public class AppointmentNotFoundException : InvalidOperationException
{
    public AppointmentNotFoundException()
    {
    }

    public AppointmentNotFoundException(string message)
        : base(message)
    {
    }

    public AppointmentNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AppointmentNotFoundException(Guid appointmentId)
        : base($"Appointment {appointmentId} was not found in this salon.")
    {
        AppointmentId = appointmentId;
    }

    public Guid? AppointmentId { get; }
}