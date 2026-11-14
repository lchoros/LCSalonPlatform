namespace Salon.Core.Paging;

/// <summary>
/// One page of a longer list (ADR 0032): the items shown, where the page sits and how many items there are in all.
/// Every list that pages uses this shape, so one pager component serves them all.
/// </summary>
/// <param name="Page">1-based, always inside the range of existing pages.</param>
/// <param name="Total">Items in the whole list, not only on this page.</param>
public sealed record Paged<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    /// <summary>At least 1: an empty list has one empty page.</summary>
    public int TotalPages => Total == 0 ? 1 : ((Total - 1) / PageSize) + 1;

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;

    /// <summary>Position of the first item on this page in the whole list, 1-based; 0 for an empty list.</summary>
    public int From => Total == 0 ? 0 : ((Page - 1) * PageSize) + 1;

    /// <summary>Position of the last item on this page in the whole list.</summary>
    public int To => Math.Min(Page * PageSize, Total);
}

/// <summary>Cuts a page out of a list. Pure. A page number that does not exist is moved to the nearest one that does.</summary>
public static class Pages
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static Paged<T> Cut<T>(IReadOnlyList<T> all, int? page, int pageSize = DefaultPageSize)
    {
        ArgumentNullException.ThrowIfNull(all);
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var totalPages = all.Count == 0 ? 1 : ((all.Count - 1) / size) + 1;
        var number = Math.Clamp(page ?? 1, 1, totalPages);
        return new Paged<T>(all.Skip((number - 1) * size).Take(size).ToArray(), number, size, all.Count);
    }
}