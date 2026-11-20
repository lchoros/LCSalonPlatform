namespace Salon.Core.Appointments;

/// <summary>What one line of a visit's history says happened (ADR 0091).</summary>
public enum VisitHistoryKind
{
    /// <summary>Booked at the desk.</summary>
    Booked,

    /// <summary>Booked by the client on the salon's booking page.</summary>
    BookedOnline,

    /// <summary>Read in from the diary file of another system.</summary>
    Imported,

    Moved,

    Confirmed,

    Arrived,

    Completed,

    NoShow,

    CancelledByClient,

    CancelledBySalon,
}

/// <summary>
/// Reads the rows the diary already keeps for every booking, move and change of status
/// (<see cref="AppointmentStatusChange"/>, ADR 0012) as the lines of a history a person can read.
/// </summary>
public static class VisitHistoryRules
{
    /// <summary>
    /// The first row of a visit has no status before it: it is the booking, and the visit's source says who made it.
    /// A row that leaves the status as it was is a move — nothing else writes one. Every other row is named by the
    /// status it led to.
    /// </summary>
    public static VisitHistoryKind KindOf(AppointmentStatus? from, AppointmentStatus to, AppointmentSource source)
    {
        if (from is null)
        {
            return source switch
            {
                AppointmentSource.Online => VisitHistoryKind.BookedOnline,
                AppointmentSource.Import => VisitHistoryKind.Imported,
                _ => VisitHistoryKind.Booked,
            };
        }

        if (from == to)
        {
            return VisitHistoryKind.Moved;
        }

        return to switch
        {
            AppointmentStatus.Confirmed => VisitHistoryKind.Confirmed,
            AppointmentStatus.Arrived => VisitHistoryKind.Arrived,
            AppointmentStatus.Completed => VisitHistoryKind.Completed,
            AppointmentStatus.NoShow => VisitHistoryKind.NoShow,
            AppointmentStatus.CancelledByClient => VisitHistoryKind.CancelledByClient,
            AppointmentStatus.CancelledBySalon => VisitHistoryKind.CancelledBySalon,
            _ => VisitHistoryKind.Booked,
        };
    }

    /// <summary>Only a cancellation carries words a person wrote; a move's reason is a marker, not a sentence.</summary>
    public static bool ShowsReason(VisitHistoryKind kind)
        => kind is VisitHistoryKind.CancelledByClient or VisitHistoryKind.CancelledBySalon;
}