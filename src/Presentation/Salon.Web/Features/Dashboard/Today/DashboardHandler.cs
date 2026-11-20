using System.Globalization;
using Salon.Core.Cqrs;
using Salon.Core.Reports;
using Salon.Services.Reports;
using Salon.Services.Settings;

namespace Salon.Web.Features.Dashboard.Today;

/// <summary>
/// Asks <see cref="ITodayService"/> for exactly the parts the role may see and says where each thing leads
/// (ADR 0084). The sums live in <c>Salon.Services</c>, shared with the till and the reports; the addresses and what
/// a role is offered are this slice's own concern, so the view decides nothing (ADR 0043).
/// </summary>
public sealed class DashboardHandler(
    ITodayService today,
    ISetupProgressService setupProgress) : IRequestHandler<DashboardQuery, DashboardModel>
{
    public async Task<DashboardModel> HandleAsync(DashboardQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var glance = await today.GetAsync(new TodayRequest(request.SeesClients, request.SeesTill), cancellationToken);
        var isNew = glance.Staff.Count == 0 && glance.Visits.Total == 0;
        var money = Money(glance, request);

        // A salon that cannot take a booking yet gets no row of zeros; money that did come in is still shown.
        var shownMoney = isNew && money is { HasAny: false } ? null : money;

        return new DashboardModel(
            glance.Day,
            isNew,
            request.ManagesSettings ? await SetupAsync(isNew, cancellationToken) : null,
            isNew ? [] : Actions(request),
            glance.Visits,
            glance.StillToCome,
            ReportRules.Utilisation(glance.BookedMinutes, glance.WorkingMinutes),
            [.. glance.Next.Select(v => new NextVisit(
                v.Id,
                v.Start,
                v.End,
                request.SeesClients ? v.ClientFirstName : null,
                v.ServiceName,
                v.StaffName,
                v.StaffColour,
                v.Status,
                request.SeesClients ? DashboardController.CardPath(v.Id) : null))],
            request.SeesClients ? DashboardController.DayListPath : null,
            [.. glance.Staff.Select(s => new OccupancyRow(s.StaffMemberId, s.Name, s.Colour, s.BookedMinutes, s.WorkingMinutes, s.Utilisation, Bar(s.Utilisation)))],
            shownMoney);
    }

    /// <summary>The owner's nudge while a required step is left (ADR 0068); nothing once the salon is set up.</summary>
    private async Task<SetupNudge?> SetupAsync(bool isNew, CancellationToken cancellationToken)
    {
        var progress = await setupProgress.GetAsync(cancellationToken);
        return progress.AllDone
            ? null
            : new SetupNudge(progress.RequiredDone, progress.Required, Bar(ReportRules.Rate(progress.RequiredDone, progress.Required)), DashboardController.GettingStartedPath, Primary: isNew);
    }

    /// <summary>Only places the role may open: the diary pages name clients, the till needs its own permissions.</summary>
    private static List<QuickAction> Actions(DashboardQuery request)
    {
        var actions = new List<QuickAction>(4);
        if (request.SeesClients)
        {
            actions.Add(new QuickAction("book", "Dashboard.Action.Book", DashboardController.BookPath, Primary: true));
            actions.Add(new QuickAction("calendar", "Dashboard.Action.Calendar", DashboardController.CalendarPath, Primary: false));
            actions.Add(new QuickAction("waitlist", "Dashboard.Action.Waitlist", DashboardController.WaitlistPath, Primary: false));
        }

        if (request.SeesTill)
        {
            actions.Add(new QuickAction("till", "Dashboard.Action.Till", DashboardController.TillPath, Primary: false));
        }

        return actions;
    }

    private static MoneyPanel? Money(TodayGlance glance, DashboardQuery request)
        => glance.Money is { } money && request.SeesTill
            ? new MoneyPanel(
                money.Takings,
                money.ByMethod,
                [.. money.Unpaid.Select(u => new UnpaidRow(u.AppointmentId, u.Start, u.ClientFirstName, u.ServiceName, u.Outstanding, DashboardController.CheckoutPath(u.AppointmentId)))],
                money.UnpaidCount,
                money.UnpaidTotal,
                DashboardController.TillPath,
                request.SeesReports ? DashboardController.ReportsPath(glance.Day) : null)
            : null;

    /// <summary>A share as a CSS percentage: never past the end of the bar, a point as the decimal sign whatever the reader's language.</summary>
    private static string Bar(decimal? percent) => Math.Clamp(percent ?? 0m, 0m, 100m).ToString("0.#", CultureInfo.InvariantCulture);
}