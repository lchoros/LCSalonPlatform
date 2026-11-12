using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Domain;

namespace Salon.Web.Features.Booking.ChooseService;

/// <summary>Step 1 of the public booking flow (ADR 0014): the salon's active services in display order. A query: tenant required, no transaction.</summary>
public sealed record ChooseServiceQuery : IQuery<IReadOnlyList<BookableService>>;

/// <summary>One tappable card; the price carries its currency so the view formats it for the reader.</summary>
public sealed record BookableService(Guid Id, string Name, string? Description, Duration Duration, Money Price);
