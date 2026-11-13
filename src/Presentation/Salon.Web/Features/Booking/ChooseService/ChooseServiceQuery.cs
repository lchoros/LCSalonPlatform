using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Domain;

namespace Salon.Web.Features.Booking.ChooseService;

/// <summary>
/// Step 1 of the public booking flow (ADR 0014): the salon's active services grouped by category like the public list
/// (ADR 0013), uncategorised ones last. A query: tenant required, no transaction.
/// </summary>
public sealed record ChooseServiceQuery : IQuery<IReadOnlyList<BookableServiceGroup>>;

/// <param name="CategoryName">Null for the uncategorised group; the view localizes its heading.</param>
public sealed record BookableServiceGroup(string? CategoryName, IReadOnlyList<BookableService> Services);

/// <summary>One tappable card; the price carries its currency so the view formats it for the reader.</summary>
public sealed record BookableService(Guid Id, string Name, string? Description, Duration Duration, Money Price);