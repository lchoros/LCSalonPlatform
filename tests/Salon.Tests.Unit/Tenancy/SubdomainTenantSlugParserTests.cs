using Microsoft.AspNetCore.Http;
using Salon.Web.Framework.Tenancy;

namespace Salon.Tests.Unit.Tenancy;

public class SubdomainTenantSlugParserTests
{
    private static readonly string[] BaseDomains = ["localhost", "salon.local"];

    [Theory]
    [InlineData("studio-20.localhost", "studio-20")]
    [InlineData("studio-20.localhost:5000", "studio-20")]
    [InlineData("Studio-20.LOCALHOST", "studio-20")]
    [InlineData("glam.salon.local", "glam")]
    public void Extracts_the_first_label_under_a_base_domain(string host, string expected)
    {
        SubdomainTenantSlugParser.TryGetSlug(new HostString(host), BaseDomains, out var slug).ShouldBeTrue();
        slug.ShouldBe(expected);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:5000")]
    [InlineData("www.localhost")]
    [InlineData("a.b.localhost")]
    [InlineData("salon.local")]
    [InlineData("evil.com")]
    [InlineData("notlocalhost")]
    [InlineData("")]
    public void Base_domain_itself_nested_labels_www_and_foreign_hosts_have_no_tenant(string host)
    {
        SubdomainTenantSlugParser.TryGetSlug(new HostString(host), BaseDomains, out _).ShouldBeFalse();
    }
}