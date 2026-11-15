using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Paging;
using Salon.Web.Framework.Lists;

namespace Salon.Web.Features.Appointments.WithoutOutcome;

/// <summary>
/// The visits of the current salon whose day is over but that nobody marked (ADR 0034), the latest first, one page
/// of them with names resolved through the tenant filter and times in the salon's zone. A query: no transaction,
/// tenant required. A page number that does not exist is moved to the nearest that does.
/// </summary>
public sealed record WithoutOutcomeQuery(int? Page) : IQuery<Paged<VisitWithoutOutcome>>
{
    public const int PageSize = 20;
}

/// <summary>One row of the list; <see cref="Day"/>, <see cref="Start"/> and <see cref="End"/> are local to the salon.</summary>
public sealed record VisitWithoutOutcome(
    Guid Id,
    LocalDate Day,
    LocalTime Start,
    LocalTime End,
    string ClientName,
    string ServiceName,
    string StaffName,
    string StaffColour,
    AppointmentStatus Status)
{
    /// <summary>The outcomes the desk may choose for this visit, in button order.</summary>
    public IReadOnlyList<VisitOutcome> Choices { get; } = VisitOutcomeRules.Choices(Status);
}

/// <param name="Visits">The page shown.</param>
/// <param name="Pager">Links to the other pages (shared list component, ADR 0032).</param>
/// <param name="Flash">What the previous request did (post-redirect-get), if anything.</param>
public sealed record WithoutOutcomeViewModel(Paged<VisitWithoutOutcome> Visits, PagerModel Pager, string? Flash);