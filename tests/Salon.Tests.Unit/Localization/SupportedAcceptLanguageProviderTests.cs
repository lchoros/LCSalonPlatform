using Microsoft.AspNetCore.Http;
using Salon.Web.Framework.Localization;

namespace Salon.Tests.Unit.Localization;

public class SupportedAcceptLanguageProviderTests
{
    private readonly SupportedAcceptLanguageProvider _provider = new();

    [Fact]
    public async Task A_neutral_Polish_tag_maps_to_pl_PL_in_quality_order()
    {
        var result = await _provider.DetermineProviderCultureResult(ContextWith("de-DE;q=0.9, pl;q=0.8, en;q=0.7"));

        result.ShouldNotBeNull();
        result.Cultures.Select(c => c.Value).ShouldBe(["pl-PL", "en-GB"]);
        result.UICultures.Select(c => c.Value).ShouldBe(["pl-PL", "en-GB"]);
    }

    [Fact]
    public async Task A_supported_tag_beyond_the_third_header_value_is_still_found()
    {
        var result = await _provider.DetermineProviderCultureResult(ContextWith("de-DE, de, fr, pl"));

        result.ShouldNotBeNull();
        result.Cultures.Select(c => c.Value).ShouldBe(["pl-PL"]);
    }

    [Fact]
    public async Task Only_unsupported_tags_yield_no_result()
    {
        var result = await _provider.DetermineProviderCultureResult(ContextWith("de-DE"));

        result.ShouldBeNull();
    }

    [Fact]
    public async Task No_header_yields_no_result()
    {
        var result = await _provider.DetermineProviderCultureResult(new DefaultHttpContext());

        result.ShouldBeNull();
    }

    private static DefaultHttpContext ContextWith(string acceptLanguage)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = acceptLanguage;
        return context;
    }
}