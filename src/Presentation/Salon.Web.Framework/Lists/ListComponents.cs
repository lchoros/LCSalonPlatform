using Microsoft.AspNetCore.WebUtilities;
using Salon.Core.Paging;

namespace Salon.Web.Framework.Lists;

/// <summary>
/// Builds the address of a list page from its filters (ADR 0032): the path plus every parameter that has a value, so
/// a link never carries an empty <c>q=</c> or <c>page=1</c> and two links to the same view are the same address.
/// </summary>
public static class ListUrl
{
    public const string SearchParameter = "q";
    public const string PageParameter = "page";

    public static string Build(string path, params (string Name, string? Value)[] parameters)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(parameters);

        var query = parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Value) && !(p.Name == PageParameter && p.Value == "1"))
            .Select(p => new KeyValuePair<string, string?>(p.Name, p.Value!.Trim()));
        return QueryHelpers.AddQueryString(path, query);
    }
}

/// <summary>
/// What the shared pager partial (<c>_Pager</c>) shows: where the page sits and the address of any other page.
/// Nothing is rendered for a list that fits on one page.
/// </summary>
/// <param name="PageUrl">The address of page n with the list's current filters kept.</param>
public sealed record PagerModel(int Page, int TotalPages, int From, int To, int Total, Func<int, string> PageUrl)
{
    public bool IsNeeded => TotalPages > 1;

    public static PagerModel For<T>(Paged<T> paged, Func<int, string> pageUrl)
    {
        ArgumentNullException.ThrowIfNull(paged);
        ArgumentNullException.ThrowIfNull(pageUrl);
        return new PagerModel(paged.Page, paged.TotalPages, paged.From, paged.To, paged.Total, pageUrl);
    }
}

/// <summary>
/// What the shared search partial (<c>_SearchBox</c>) shows: a GET form, so a search is an address that can be shared
/// and the back button works.
/// </summary>
/// <param name="Action">The list's own path.</param>
/// <param name="Value">What was searched for, shown again in the field.</param>
/// <param name="Label">The field's accessible name, already localized.</param>
/// <param name="Keep">Other filters to carry along as hidden fields; blank values are left out.</param>
/// <param name="ClearUrl">Where "clear" leads: the list without the search but with the other filters.</param>
public sealed record SearchBoxModel(string Action, string? Value, string Label, IReadOnlyList<(string Name, string? Value)> Keep, string ClearUrl);

/// <summary>What the shared filter partial (<c>_FilterTiles</c>) shows: one tile per choice, the chosen one marked.</summary>
/// <param name="Label">The group's accessible name, already localized.</param>
public sealed record FilterTilesModel(string Label, IReadOnlyList<FilterTile> Tiles);

/// <param name="Key">A stable value for tests and styling hooks; not shown.</param>
/// <param name="Accent">Which of the five accent colours the tile carries; 0 for the neutral "all" tile.</param>
public sealed record FilterTile(string Key, string Label, string Url, bool Selected, int Accent = 0);