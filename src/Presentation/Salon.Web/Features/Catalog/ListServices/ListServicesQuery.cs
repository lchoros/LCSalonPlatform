using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Domain;

namespace Salon.Web.Features.Catalog.ListServices;

/// <summary>Every service of the current salon (active and inactive), grouped by category with the uncategorised ones last. A query: no transaction, tenant required.</summary>
public sealed record ListServicesQuery : IQuery<IReadOnlyList<ServiceGroupItem>>;

/// <param name="CategoryName">Null for the uncategorised group; the view localizes its heading.</param>
/// <param name="CategoryIsActive">False shows a badge next to the heading so the owner knows why the category is missing from the pickers.</param>
public sealed record ServiceGroupItem(string? CategoryName, bool CategoryIsActive, IReadOnlyList<ServiceListItem> Services);

/// <summary>One row of the admin list; the price carries its currency so the view can format it for the reader.</summary>
public sealed record ServiceListItem(Guid Id, string Name, Duration Duration, int BufferAfterMinutes, int ProcessingMinutes, Money Price, bool IsActive);

/// <param name="Groups">In display order; empty shows the empty state.</param>
/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
public sealed record ListServicesViewModel(IReadOnlyList<ServiceGroupItem> Groups, string? Flash);