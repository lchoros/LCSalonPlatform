namespace Salon.Core.Appointments;

/// <summary>Thrown when code asks for a status change the state machine (ADR 0012) does not allow.</summary>
public class InvalidAppointmentTransitionException : InvalidOperationException
{
    public InvalidAppointmentTransitionException()
    {
    }

    public InvalidAppointmentTransitionException(string message)
        : base(message)
    {
    }

    public InvalidAppointmentTransitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public InvalidAppointmentTransitionException(AppointmentStatus from, AppointmentStatus to)
        : base($"An appointment cannot go from {from} to {to}.")
    {
        From = from;
        To = to;
    }

    public AppointmentStatus? From { get; }

    public AppointmentStatus? To { get; }
}