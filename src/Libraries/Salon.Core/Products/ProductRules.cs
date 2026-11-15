namespace Salon.Core.Products;

/// <summary>
/// The pure rules a product must satisfy (ADR 0036), shared by the slices that add and change one. The validators turn
/// each failure into a localized message; these methods only decide.
/// </summary>
public static class ProductRules
{
    public const int NameMaxLength = 150;
    public const int BrandMaxLength = 100;
    public const int CodeMaxLength = 50;
    public const decimal MaxPrice = 100_000m;
    public const int MaxLowStockAt = 10_000;

    /// <summary>The stored form of a name: trimmed, never null.</summary>
    public static string NormalizeName(string? name) => name?.Trim() ?? string.Empty;

    /// <summary>The stored form of an optional text: trimmed, null when blank.</summary>
    public static string? NormalizeOptional(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public static bool HasName(string? name) => !string.IsNullOrWhiteSpace(name);

    /// <summary>Length after trimming; a missing name passes here and fails <see cref="HasName"/>, so it is reported once.</summary>
    public static bool NameFits(string? name) => name is null || name.Trim().Length <= NameMaxLength;

    public static bool BrandFits(string? brand) => brand is null || brand.Trim().Length <= BrandMaxLength;

    public static bool CodeFits(string? code) => code is null || code.Trim().Length <= CodeMaxLength;

    /// <summary>No price means the salon does not sell it; a price is not negative, has at most two decimals and stays under <see cref="MaxPrice"/>.</summary>
    public static bool IsPriceValid(decimal? price) => price is null || (price >= 0m && price <= MaxPrice && decimal.Round(price.Value, 2) == price);

    /// <summary>No threshold never warns; otherwise zero ("warn when it is gone") up to <see cref="MaxLowStockAt"/>.</summary>
    public static bool IsLowStockAtValid(int? lowStockAt) => lowStockAt is null or (>= 0 and <= MaxLowStockAt);

    /// <summary>
    /// A literal for a case-insensitive whole-name match with <c>ILIKE</c>: the wildcard characters of the pattern are
    /// escaped so "100%" matches itself only.
    /// </summary>
    public static string LikeLiteral(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
    }

    /// <summary>The words of a search, lower-cased; a product matches when each occurs in its name, brand or code.</summary>
    public static IReadOnlyList<string> Words(string? search)
        => string.IsNullOrWhiteSpace(search)
            ? []
            : search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(w => w.ToLowerInvariant()).ToArray();

    public static bool Matches(Product product, IReadOnlyList<string> words)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(words);
        var text = string.Join(' ', product.Name, product.Brand, product.Code).ToLowerInvariant();
        return words.All(w => text.Contains(w, StringComparison.Ordinal));
    }
}