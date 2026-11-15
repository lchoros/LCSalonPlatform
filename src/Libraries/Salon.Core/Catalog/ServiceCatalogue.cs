namespace Salon.Core.Catalog;

/// <summary>
/// One line of a service list (ADR 0032): a service on its own, or a group of variants of one service shown under a
/// common title with the range of their prices.
/// </summary>
/// <param name="Category">Null for the uncategorised entries, which come last.</param>
/// <param name="Title">The service's name, or the title the variants share.</param>
/// <param name="Services">One service, or the variants in display order; never empty.</param>
public sealed record CatalogueEntry(ServiceCategory? Category, string Title, IReadOnlyList<Service> Services, bool IsVariantGroup)
{
    public decimal MinPrice => Services.Min(s => s.Price);

    public decimal MaxPrice => Services.Max(s => s.Price);
}

/// <summary>
/// Turns a salon's services into the entries of a list (ADR 0032). Pure. Order is the catalogue's own
/// (<see cref="ServiceGrouping"/>): categories by sort order, services by sort order then name, uncategorised last.
/// Services that share a <see cref="Service.VariantGroup"/> inside one category become one entry at the place of the
/// first of them. A search keeps the services in which every word occurs, whatever the case.
/// </summary>
public static class ServiceCatalogue
{
    public static IReadOnlyList<CatalogueEntry> Entries(IEnumerable<Service> services, IEnumerable<ServiceCategory> categories, Guid? categoryId = null, string? search = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(categories);

        var words = Words(search);
        var entries = new List<CatalogueEntry>();
        foreach (var group in ServiceGrouping.Group(services, categories))
        {
            if (categoryId is { } wanted && group.Category?.Id != wanted)
            {
                continue;
            }

            var variantEntries = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var service in group.Services.Where(s => Matches(s, words)))
            {
                if (service.VariantGroup is not { Length: > 0 } title)
                {
                    entries.Add(new CatalogueEntry(group.Category, service.Name, [service], false));
                }
                else if (variantEntries.TryGetValue(title, out var index))
                {
                    entries[index] = entries[index] with { Services = [.. entries[index].Services, service] };
                }
                else
                {
                    variantEntries[title] = entries.Count;
                    entries.Add(new CatalogueEntry(group.Category, title, [service], true));
                }
            }
        }

        return entries;
    }

    /// <summary>The words of a search, without the spaces around and between them; empty for a blank search.</summary>
    public static IReadOnlyList<string> Words(string? search)
        => string.IsNullOrWhiteSpace(search) ? [] : search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool Matches(Service service, IReadOnlyList<string> words)
        => words.All(word =>
            service.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
            || (service.VariantGroup?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false)
            || (service.VariantLabel?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false)
            || (service.Description?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false));
}