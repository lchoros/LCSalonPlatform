using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Services.Payments;
using Salon.Services.Reports;

namespace Salon.Web.Features.Dashboard.Today;

/// <summary>
/// Today at a glance for the person who is signed in (ADR 0084): the flags say what their role may open, so the
/// handler reads and links nothing else. A query: no transaction, tenant required.
/// </summary>
/// <param name="SeesClients">May open the diary pages, which name clients: first names and the way to a visit's card.</param>
/// <param name="SeesTill">May open the till: the money of the day and the checkout of an unpaid visit.</param>
/// <param name="SeesReports">May open the reports, where every unpaid visit is listed.</param>
/// <param name="ManagesSettings">May open the getting started page.</param>
public sealed record DashboardQuery(bool SeesClients, bool SeesTill, bool SeesReports, bool ManagesSettings) : IQuery<DashboardModel>;

/// <param name="Day">The salon's current local day.</param>
/// <param name="IsNewSalon">Nobody takes bookings and nothing is booked today: there is no day to show yet.</param>
/// <param name="Setup">Where set-up stands, for the owner of a salon that is not set up yet; null otherwise.</param>
/// <param name="Actions">The everyday places this role may open, the first one the primary button.</param>
/// <param name="Occupancy">Booked time of everybody over their working time today, percent; null while nobody is in.</param>
/// <param name="DayListPath">Where the rest of today's visits are; null for a role that may not open the diary.</param>
/// <param name="Money">Null for a role that may not open the till, and for a new salon with no money to show.</param>
public sealed record DashboardModel(
    LocalDate Day,
    bool IsNewSalon,
    SetupNudge? Setup,
    IReadOnlyList<QuickAction> Actions,
    TodayVisits Visits,
    int StillToCome,
    decimal? Occupancy,
    IReadOnlyList<NextVisit> Next,
    string? DayListPath,
    IReadOnlyList<OccupancyRow> Staff,
    MoneyPanel? Money)
{
    /// <summary>Visits still to come that the list above does not name.</summary>
    public int MoreToCome => StillToCome - Next.Count;

    /// <summary>Nothing at all is booked for today, which reads differently from a day whose visits are over.</summary>
    public bool NothingBooked => Visits.Total == 0;
}

/// <param name="BarWidth">How far the progress bar is filled, as a CSS percentage.</param>
/// <param name="Path">The getting started page.</param>
/// <param name="Primary">Whether the way there is the page's main button: it is while the salon has no day to show.</param>
public sealed record SetupNudge(int Done, int Required, string BarWidth, string Path, bool Primary)
{
    /// <summary>The one strong button of a page that has nothing else to offer yet; a quiet one beside the day's own actions.</summary>
    public string ButtonClass => Primary ? "btn btn-compact btn-primary" : "btn btn-compact btn-secondary";
}

/// <param name="Name">The last segment of the action's test id and resource key.</param>
public sealed record QuickAction(string Name, string LabelKey, string Path, bool Primary)
{
    public string ButtonClass => Primary ? "btn btn-compact btn-primary" : "btn btn-compact btn-secondary";
}

/// <param name="ClientFirstName">Null for a role that may not see clients.</param>
/// <param name="CardPath">The visit's card; null for a role that may not open it.</param>
public sealed record NextVisit(
    Guid Id,
    LocalTime Start,
    LocalTime End,
    string? ClientFirstName,
    string ServiceName,
    string StaffName,
    string StaffColour,
    AppointmentStatus Status,
    string? CardPath);

/// <param name="Percent">Booked time over working time today; null for somebody who is not in.</param>
/// <param name="BarWidth">The same, never past the end of the bar, as a CSS percentage and an <c>aria-valuenow</c>.</param>
public sealed record OccupancyRow(Guid StaffMemberId, string Name, string Colour, int BookedMinutes, int WorkingMinutes, decimal? Percent, string BarWidth)
{
    public bool IsIn => WorkingMinutes > 0;
}

/// <param name="TillPath">The till of today.</param>
/// <param name="ReportsPath">Today's report, which lists every unpaid visit; null for a role that may not open the reports.</param>
public sealed record MoneyPanel(
    Money Takings,
    IReadOnlyList<MethodTotal> ByMethod,
    IReadOnlyList<UnpaidRow> Unpaid,
    int UnpaidCount,
    Money UnpaidTotal,
    string TillPath,
    string? ReportsPath)
{
    public bool HasAny => ByMethod.Count > 0 || UnpaidCount > 0;

    /// <summary>More visits are unpaid than the list names.</summary>
    public bool HasMoreUnpaid => UnpaidCount > Unpaid.Count;
}

/// <param name="CheckoutPath">Where the payment is taken.</param>
public sealed record UnpaidRow(Guid AppointmentId, LocalTime Start, string ClientFirstName, string ServiceName, Money Outstanding, string CheckoutPath);