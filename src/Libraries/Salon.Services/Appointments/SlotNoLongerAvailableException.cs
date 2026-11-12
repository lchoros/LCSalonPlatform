namespace Salon.Services.Appointments;

/// <summary>
/// The requested start is not bookable: it is no longer in the slot list, or Postgres refused the overlap because another
/// booking of the same staff member won the race (ADR 0012). The UI shows a friendly message and a fresh slot list.
/// </summary>
public class SlotNoLongerAvailableException : InvalidOperationException
{
    public SlotNoLongerAvailableException()
        : base("The chosen time has just been taken; please pick another slot.")
    {
    }

    public SlotNoLongerAvailableException(string message)
        : base(message)
    {
    }

    public SlotNoLongerAvailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}