using NodaTime;

using Salon.Core.Cqrs;
using Salon.Services.Clients;

namespace Salon.Web.Features.Appointments.BookForClient;

/// <summary>
/// Everything the "book for a client" page needs for its current selection (ADR 0012): client matches for the search
/// box, the chosen client, the active services, the bookable staff and — once a service and a date are chosen — the slots
/// the availability engine offers, already in the salon's zone. A query: no transaction, tenant required.
/// </summary>
/// <param name="Query">What was typed in the client search box; null hides the match list.</param>
/// <param name="StaffMemberId">Null means "any available".</param>
/// <param name="Date">Null means today in the salon's zone.</param>
public sealed record BookForClientFormQuery(string? Query, Guid? ClientId, Guid? ServiceId, Guid? StaffMemberId, LocalDate? Date) : IQuery<BookForClientForm>;

/// <param name="Query">Echoed in the search box.</param>
/// <param name="ClientMatches">Up to <see cref="BookForClientFormHandler.MaxMatches"/> clients for <paramref name="Query"/>; empty with a query shows the no-match line.</param>
/// <param name="Client">The chosen client, or null when none (or not one of this salon's).</param>
/// <param name="Services">Active services in catalogue order.</param>
/// <param name="Staff">Active, bookable staff in display order.</param>
/// <param name="ServiceId">The chosen service, when it is one of <paramref name="Services"/>.</param>
/// <param name="StaffMemberId">The chosen staff member; null is "any available".</param>
/// <param name="Day">The day the slots are for.</param>
/// <param name="Slots">Free starts for the selection; empty when no service is chosen or nothing is free.</param>
public sealed record BookForClientForm(
    string? Query,
    IReadOnlyList<ClientSummary> ClientMatches,
    ClientSummary? Client,
    IReadOnlyList<ServiceOption> Services,
    IReadOnlyList<StaffOption> Staff,
    Guid? ServiceId,
    Guid? StaffMemberId,
    LocalDate Day,
    IReadOnlyList<SlotOption> Slots)
{
    /// <summary>True once a service is chosen, so the page shows the slot section (with its empty state when nothing is free).</summary>
    public bool ShowSlots => ServiceId is not null;
}

public sealed record ServiceOption(Guid Id, string Name, int DurationMinutes);

public sealed record StaffOption(Guid Id, string DisplayName);

/// <param name="Start">The instant the slot form posts; <see cref="LocalStart"/> is what the button shows.</param>
public sealed record SlotOption(Guid StaffMemberId, string StaffName, Instant Start, LocalTime LocalStart);

/// <param name="Form">The page state.</param>
/// <param name="Error">A refused booking (slot taken meanwhile), already localized.</param>
public sealed record BookForClientViewModel(BookForClientForm Form, string? Error);