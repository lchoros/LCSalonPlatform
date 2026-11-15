namespace Salon.Services.Localization;

/// <summary>
/// Text that carries a count (ADR 0035). Such a key has two values: <c>Key.One</c> for exactly one and <c>Key.Many</c>
/// for every other count, zero included. The count is argument <c>{0}</c> in both, the caller's arguments follow.
/// English needs no more than that. A language with more forms (Polish: 1 wizyta, 2 wizyty, 5 wizyt) writes its
/// <c>.Many</c> value so that it reads right with any number ("Wizyty: {0}").
/// </summary>
public static class LocalizationPlurals
{
    public const string OneSuffix = ".One";
    public const string ManySuffix = ".Many";

    /// <summary>The <c>.One</c> or <c>.Many</c> value of <paramref name="key"/>, formatted with the count first.</summary>
    public static string Plural(this ILocalizationService localizer, string key, int count, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(args);

        return localizer.Format(key + (count == 1 ? OneSuffix : ManySuffix), [count, .. args]);
    }
}