using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using NSubstitute;

using Salon.Web.Framework.Tenancy;

namespace Salon.Tests.Unit.Tenancy;

/// <summary>Links in SMS and email (ADR 0014) come from configuration, not from the request: scheme, first base domain, slug, optional port.</summary>
public class PublicUrlBuilderTests
{
    private const string Token = "test-cancel-token-not-a-secret";

    [Fact]
    public void Https_and_the_first_base_domain_by_default()
    {
        var builder = Build(new TenancyOptions { BaseDomains = ["salon.example", "salon.pl"] }, "Production");

        builder.BookingUrl("studio-20").ShouldBe("https://studio-20.salon.example/book");
        builder.CancelBookingUrl("studio-20", Token).ShouldBe($"https://studio-20.salon.example/book/cancel/{Token}");
    }

    [Fact]
    public void Development_without_a_configured_scheme_uses_http_and_the_port()
    {
        var builder = Build(new TenancyOptions { BaseDomains = ["localhost"], PublicPort = 5227 }, Environments.Development);

        builder.BookingUrl("studio-20").ShouldBe("http://studio-20.localhost:5227/book");
    }

    [Fact]
    public void A_configured_scheme_wins_over_the_environment()
    {
        var builder = Build(new TenancyOptions { BaseDomains = ["localhost"], PublicScheme = "https" }, Environments.Development);

        builder.BookingUrl("Studio-20").ShouldBe("https://studio-20.localhost/book");
    }

    [Fact]
    public void An_empty_base_domain_list_falls_back_to_the_default_and_the_token_is_escaped()
    {
        var builder = Build(new TenancyOptions(), "Production");

        builder.CancelBookingUrl("glam", "a b/c").ShouldBe("https://glam.localhost/book/cancel/a%20b%2Fc");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void A_blank_slug_or_token_is_refused(string blank)
    {
        var builder = Build(new TenancyOptions(), "Production");

        Should.Throw<ArgumentException>(() => builder.BookingUrl(blank));
        Should.Throw<ArgumentException>(() => builder.CancelBookingUrl("glam", blank));
    }

    private static PublicUrlBuilder Build(TenancyOptions options, string environment)
    {
        var host = Substitute.For<IHostEnvironment>();
        host.EnvironmentName.Returns(environment);
        return new PublicUrlBuilder(Options.Create(options), host);
    }
}