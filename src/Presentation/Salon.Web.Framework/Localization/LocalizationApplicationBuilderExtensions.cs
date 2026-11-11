using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Localization;
using Salon.Services.Localization;

namespace Salon.Web.Framework.Localization;

public static class LocalizationApplicationBuilderExtensions
{
    /// <summary>
    /// Loads and validates every localization resource now, then sets the request culture and UI culture (ADR 0006) in
    /// this order: <c>?culture=</c> query string, the ASP.NET culture cookie, the tenant's culture, then
    /// <c>Accept-Language</c> (neutral tags mapped to the supported specific culture); unsupported values are skipped and
    /// <see cref="SupportedCultures.Default"/> applies last. Must be placed <b>after</b> <c>UseSalonTenancy()</c>,
    /// because <see cref="TenantCultureProvider"/> reads the resolved tenant.
    /// </summary>
    public static IApplicationBuilder UseSalonLocalization(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Packaging mistakes (a de-DE file, a duplicate key, invalid JSON) stop the host here instead of failing every request.
        _ = app.ApplicationServices.GetRequiredService<ILocalizationService>().Cultures;

        var cultures = SupportedCultures.All.ToArray();
        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(SupportedCultures.Default),
            FallBackToParentCultures = true,
            FallBackToParentUICultures = true,
            RequestCultureProviders =
            [
                new QueryStringRequestCultureProvider(),
                new CookieRequestCultureProvider(),
                new TenantCultureProvider(),
                new SupportedAcceptLanguageProvider(),
            ],
        };
        options.AddSupportedCultures(cultures).AddSupportedUICultures(cultures);

        return app.UseRequestLocalization(options);
    }
}