using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Primitives;
using Salon.Core.Localization;

namespace Salon.Web.Framework.Localization;

/// <summary>
/// <see cref="AcceptLanguageHeaderRequestCultureProvider"/> whose candidates are mapped through
/// <see cref="SupportedCultures.Match"/>, so a browser sending the neutral <c>pl</c> gets <c>pl-PL</c>. The built-in
/// parent fallback only walks upwards (pl-PL → pl) and would never select a specific culture from a neutral tag.
/// Unmatched tags are dropped; an empty result lets the default culture apply.
/// </summary>
public sealed class SupportedAcceptLanguageProvider : AcceptLanguageHeaderRequestCultureProvider
{
    public SupportedAcceptLanguageProvider()
    {
        // The base class truncates the header before our mapping runs; its default of 3 would drop "pl" in "de-DE, de, fr, pl".
        MaximumAcceptLanguageHeaderValuesToTry = 10;
    }

    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var result = await base.DetermineProviderCultureResult(httpContext).ConfigureAwait(false);
        if (result is null)
        {
            return null;
        }

        var cultures = result.Cultures
            .Select(c => SupportedCultures.Match(c.Value))
            .OfType<string>()
            .Select(c => new StringSegment(c))
            .ToList();

        return cultures.Count == 0 ? null : new ProviderCultureResult(cultures, cultures);
    }
}