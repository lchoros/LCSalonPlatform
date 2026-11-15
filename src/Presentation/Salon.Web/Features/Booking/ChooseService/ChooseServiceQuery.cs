using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Paging;

namespace Salon.Web.Features.Booking.ChooseService;

/// <summary>
/// Step 1 of the public booking flow (ADR 0014, ADR 0032): the salon's active services as list entries, a service on
/// its own or a group of variants, filtered by category and search and cut into pages. A query: tenant required, no
/// transaction.
/// </summary>
/// <param name="CategoryId">Only this category; an id the salon does not have is ignored.</param>
/// <param name="Search">Words that must all occur in a service.</param>
/// <param name="Page">1-based; out of range moves to the nearest page.</param>
public sealed record ChooseServiceQuery(Guid? CategoryId = null, string? Search = null, int? Page = null) : IQuery<ServiceChoice>;

/// <param name="Entries">The page of entries, in the catalogue's order.</param>
/// <param name="Categories">Categories that have at least one active service, in display order, for the filter.</param>
/// <param name="CategoryId">The category the list is narrowed to, when it is one the salon has.</param>
/// <param name="Search">The search as it was applied, trimmed; null without one.</param>
/// <param name="TotalServices">Active services before any filter: a short list needs no search or filter.</param>
public sealed record ServiceChoice(
    Paged<BookableEntry> Entries,
    IReadOnlyList<BookableCategory> Categories,
    Guid? CategoryId,
    string? Search,
    int TotalServices)
{
    /// <summary>More services than fit a phone screen or two: from here on the page offers search, filter and pages.</summary>
    public const int FilterThreshold = 8;

    public const int PageSize = 12;

    public bool ShowFilters => TotalServices > FilterThreshold;

    public bool IsFiltered => CategoryId is not null || Search is not null;
}

/// <param name="Accent">Which of the five accent colours the category carries on this page.</param>
public sealed record BookableCategory(Guid Id, string Name, int Accent);

/// <param name="CategoryName">Null for the uncategorised entries; the view localizes their heading.</param>
/// <param name="Title">The service's name, or the title its variants share.</param>
/// <param name="Services">One service, or the variants; a variant's name is its label.</param>
public sealed record BookableEntry(
    string? CategoryName,
    int Accent,
    string Title,
    bool IsVariantGroup,
    IReadOnlyList<BookableService> Services,
    Money MinPrice,
    Money MaxPrice);

/// <summary>One bookable service; the price carries its currency so the view formats it for the reader.</summary>
public sealed record BookableService(Guid Id, string Name, string? Description, Duration Duration, Money Price);