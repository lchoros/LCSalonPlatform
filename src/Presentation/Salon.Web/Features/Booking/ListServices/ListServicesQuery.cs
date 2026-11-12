using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Domain;

namespace Salon.Web.Features.Booking.ListServices;

/// <summary>The current salon's active services in display order, grouped by category with the uncategorised ones last (ADR 0013). A query: no transaction, tenant required.</summary>
public sealed record ListServicesQuery : IQuery<IReadOnlyList<ServiceListGroup>>;

/// <param name="CategoryName">Null for the uncategorised group; the view localizes its heading and shows headings only when the salon has at least one category.</param>
public sealed record ServiceListGroup(string? CategoryName, IReadOnlyList<ServiceListItem> Services);

/// <summary>One row of the public list; the price carries its currency so the view can format it for the reader.</summary>
public sealed record ServiceListItem(Guid Id, string Name, string? Description, Duration Duration, Money Price);