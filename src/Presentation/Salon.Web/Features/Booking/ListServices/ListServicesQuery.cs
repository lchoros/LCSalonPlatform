using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Domain;

namespace Salon.Web.Features.Booking.ListServices;

/// <summary>The current salon's active services in display order. A query: no transaction, tenant required.</summary>
public sealed record ListServicesQuery : IQuery<IReadOnlyList<ServiceListItem>>;

/// <summary>One row of the public list; the price carries its currency so the view can format it for the reader.</summary>
public sealed record ServiceListItem(Guid Id, string Name, string? Description, Duration Duration, Money Price);