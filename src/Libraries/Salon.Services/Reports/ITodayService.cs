using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Domain;
using Salon.Services.Payments;

namespace Salon.Services.Reports;

/// <summary>
/// The salon's current day at a glance (ADR 0084): the visits that start today in the salon's zone, who is next, how
/// full each person's day is and, when asked for, the money of the day. Read only. It adds up with the rules the
/// till and the reports use, so the three never disagree: money is the day's ledger lines by the till's own rule,
/// occupancy is booked time over the rota less time off, and a completed visit is unpaid exactly when the report of
/// today names it.
/// </summary>
public interface ITodayService
{
    /// <summary>Today in the salon's zone, with only the parts <paramref name="request"/> asks for.</summary>
    Task<TodayGlance> GetAsync(TodayRequest request, CancellationToken cancellationToken = default);
}

/// <summary>What the reader may see, so nothing else is read.</summary>
/// <param name="ClientNames">First names beside the next visits and the unpaid ones; without it they stay empty.</param>
/// <param name="Money">The takings of the day and the completed visits nobody took payment for.</param>
public sealed record TodayRequest(bool ClientNames, bool Money);

/// <param name="Day">The salon's current local day.</param>
/// <param name="Next">Visits of today that still hold their time and have not ended, soonest first, at most <see cref="MaxNext"/>.</param>
/// <param name="StillToCome">How many such visits there are in all.</param>
/// <param name="Staff">Every person who takes bookings, in display order.</param>
/// <param name="Money">Null unless it was asked for.</param>
public sealed record TodayGlance(
    LocalDate Day,
    TodayVisits Visits,
    IReadOnlyList<UpcomingVisit> Next,
    int StillToCome,
    IReadOnlyList<StaffOccupancy> Staff,
    TodayMoney? Money)
{
    public const int MaxNext = 5;
    public const int MaxUnpaidListed = 5;

    /// <summary>Booked minutes of everybody with working time today.</summary>
    public int BookedMinutes => Staff.Where(s => s.WorkingMinutes > 0).Sum(s => s.BookedMinutes);

    /// <summary>Working minutes of everybody today.</summary>
    public int WorkingMinutes => Staff.Sum(s => s.WorkingMinutes);
}

/// <summary>Every visit that starts today, by what became of it; <paramref name="Cancelled"/> is by the client or by the salon.</summary>
public sealed record TodayVisits(int Total, int Booked, int Confirmed, int Arrived, int Completed, int NoShows, int Cancelled);

/// <param name="Start">In the salon's zone.</param>
/// <param name="ClientFirstName">Empty unless client names were asked for. Never more than the first name.</param>
public sealed record UpcomingVisit(
    Guid Id,
    LocalTime Start,
    LocalTime End,
    string ClientFirstName,
    string ServiceName,
    string StaffName,
    string StaffColour,
    AppointmentStatus Status);

/// <param name="BookedMinutes">Visits of today that took place or still stand; no-shows and cancellations are left out.</param>
/// <param name="WorkingMinutes">Today's rota less time off; zero for somebody who is not in today.</param>
/// <param name="Utilisation">Percent; null for somebody without working time today.</param>
public sealed record StaffOccupancy(Guid StaffMemberId, string Name, string Colour, int BookedMinutes, int WorkingMinutes, decimal? Utilisation);

/// <param name="Takings">Everything that came in today less everything that went out, all methods: the till's own figure.</param>
/// <param name="ByMethod">Net per method, methods nobody used left out.</param>
/// <param name="Unpaid">Completed visits of today nobody took payment for, oldest first, at most <see cref="TodayGlance.MaxUnpaidListed"/>.</param>
/// <param name="UnpaidCount">How many there are in all.</param>
/// <param name="UnpaidTotal">What is left to pay for all of them.</param>
public sealed record TodayMoney(Money Takings, IReadOnlyList<MethodTotal> ByMethod, IReadOnlyList<UnpaidToday> Unpaid, int UnpaidCount, Money UnpaidTotal);

/// <param name="Start">In the salon's zone.</param>
/// <param name="ClientFirstName">Empty unless client names were asked for.</param>
/// <param name="Outstanding">The booked price less what the salon already holds toward it.</param>
public sealed record UnpaidToday(Guid AppointmentId, LocalTime Start, string ClientFirstName, string ServiceName, Money Outstanding);