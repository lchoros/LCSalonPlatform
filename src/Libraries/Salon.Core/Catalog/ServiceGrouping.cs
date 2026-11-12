namespace Salon.Core.Catalog;

/// <summary>
/// Groups services under their categories the way both lists show them (ADR 0013): categories in sort order (then name),
/// services in sort order (then name) inside each, categories without services left out, and the services without a
/// category (or with a category the salon no longer has) in one last group with a null category. Pure: the views localize
/// the "uncategorised" heading.
/// </summary>
public static class ServiceGrouping
{
    public static IReadOnlyList<ServiceGroup> Group(IEnumerable<Service> services, IEnumerable<ServiceCategory> categories)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(categories);

        var known = categories.ToDictionary(c => c.Id);
        var byCategory = services
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToLookup(s => s.CategoryId is { } id && known.ContainsKey(id) ? id : (Guid?)null);

        var groups = known.Values
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Where(c => byCategory.Contains(c.Id))
            .Select(c => new ServiceGroup(c, byCategory[c.Id].ToList()))
            .ToList();

        if (byCategory.Contains(null))
        {
            groups.Add(new ServiceGroup(null, byCategory[null].ToList()));
        }

        return groups;
    }
}

/// <param name="Category">Null for the uncategorised group, which is always last.</param>
/// <param name="Services">In display order; never empty.</param>
public sealed record ServiceGroup(ServiceCategory? Category, IReadOnlyList<Service> Services);