using Salon.Core.Appointments;
using Salon.Services.Appointments.Import;
using Salon.Services.Catalog.Import;
using Salon.Services.Clients.Import;
using Salon.Services.Payments;

namespace Salon.Web.Framework.Ui;

/// <summary>
/// Which colours and states a status wears (ADR 0043). One place, so the same status looks the same on every page and
/// a view does not decide it for itself. Only class names: what the classes look like is the stylesheet's business.
/// </summary>
public static class Badges
{
    private const string Neutral = "bg-stone-100 text-stone-700";
    private const string Done = "bg-stone-200 text-stone-800";
    private const string Good = "bg-green-100 text-green-900";
    private const string Info = "bg-blue-100 text-blue-900";
    private const string Warn = "bg-amber-100 text-amber-900";
    private const string Bad = "bg-red-100 text-red-900";

    /// <summary>A visit's status on its own card: every status has its colour.</summary>
    public static string Appointment(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Confirmed => Info,
        AppointmentStatus.Arrived => Good,
        AppointmentStatus.Completed => Done,
        AppointmentStatus.NoShow or AppointmentStatus.CancelledByClient or AppointmentStatus.CancelledBySalon => Bad,
        _ => Neutral,
    };

    /// <summary>
    /// A visit's status in a list of the day: only what the desk acts on next stands out (confirmed, arrived); the
    /// rest stays quiet so a full day is not a wall of colour.
    /// </summary>
    public static string AppointmentInList(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Confirmed => Info,
        AppointmentStatus.Arrived => Good,
        _ => Neutral,
    };

    /// <summary>
    /// A visit's status in a client's history: what matters looking back. Completed is good, a no-show is bad, a
    /// cancellation is done with, and a visit still to come is information.
    /// </summary>
    public static string AppointmentInHistory(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Completed => Good,
        AppointmentStatus.NoShow => Bad,
        AppointmentStatus.CancelledByClient or AppointmentStatus.CancelledBySalon => "bg-stone-200 text-stone-700",
        AppointmentStatus.Confirmed or AppointmentStatus.Arrived => Info,
        _ => Neutral,
    };

    /// <summary>A fee: owed is a warning, paid is good, waived is done.</summary>
    public static string Fee(FeeStatus status) => status switch
    {
        FeeStatus.Paid => Good,
        FeeStatus.Waived => Done,
        _ => Warn,
    };

    /// <summary>A row of a client import: new is good, already there is done, unusable is bad.</summary>
    public static string ImportRow(ImportRowStatus status) => status switch
    {
        ImportRowStatus.New => Good,
        ImportRowStatus.Duplicate => Done,
        _ => Bad,
    };

    /// <summary>A step of setting a salon up (ADR 0068): done, left to do, or one the salon may leave.</summary>
    public static string SetupStep(bool done, bool optional) => done ? Good : optional ? Neutral : Warn;

    /// <summary>A login on the team page (ADR 0076): quiet while it is on, red while it is off.</summary>
    public static string TeamLogin(bool active) => active ? Neutral : Bad;

    /// <summary>A row of a diary file (ADR 0067): added, already there, cancelled, of earlier today, cancelled since and to look at, or not readable.</summary>
    public static string AppointmentImportRow(AppointmentImportStatus status) => status switch
    {
        AppointmentImportStatus.New => Good,
        AppointmentImportStatus.CancelledSince => Warn,
        AppointmentImportStatus.Invalid => Bad,
        _ => Done,
    };

    /// <summary>A row of a price list file (ADR 0066): added, changed, as it was, left out because it is said twice, or not readable.</summary>
    public static string ServiceImportRow(ServiceImportStatus status) => status switch
    {
        ServiceImportStatus.New => Good,
        ServiceImportStatus.Changed => Warn,
        ServiceImportStatus.Unchanged or ServiceImportStatus.Repeated => Done,
        _ => Bad,
    };

    /// <summary>
    /// A day tile of the booking pages: the chosen day, a day with free times, or a day without. Days without free
    /// times stay tappable but muted (<c>.is-dim</c> keeps AA contrast on white).
    /// </summary>
    /// <summary>A time kept for a waiting client on the diary (ADR 0069): something the desk should notice.</summary>
    public static string KeptTime => Warn;

    public static string DayTile(bool chosen, bool hasFreeTimes) => chosen ? "is-on" : hasFreeTimes ? "is-off" : "is-dim";

    /// <summary>The answer the desk gives most often is the primary button; the other is secondary.</summary>
    public static string OutcomeButton(VisitOutcome outcome)
        => outcome == VisitOutcome.Came ? "btn btn-compact btn-primary" : "btn btn-compact btn-secondary";
}