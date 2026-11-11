using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using Salon.Core.Tenancy;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Tenancy;

namespace Salon.Tests.Unit.Tenancy;

public class TenantResolutionMiddlewareTests
{
    private static readonly Guid TenantA = Guid.CreateVersion7();
    private static readonly TenantInfo Studio = new(TenantA, "studio-20", "Studio 20", "Europe/London", "en-GB", "GBP", true);

    private readonly ITenantLookup _lookup = Substitute.For<ITenantLookup>();
    private readonly IMutableTenantContext _tenantContext = Substitute.For<IMutableTenantContext>();
    private bool _nextCalled;

    private TenantResolutionMiddleware CreateMiddleware() => new(
        _ =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        },
        Options.Create(new TenancyOptions()),
        NullLogger<TenantResolutionMiddleware>.Instance);

    private static DefaultHttpContext Request(string host, Guid? claimTenant = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        if (claimTenant is { } id)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", id.ToString())], "test"));
        }

        return context;
    }

    [Fact]
    public async Task Known_subdomain_sets_the_tenant_and_continues()
    {
        _lookup.FindBySlugAsync("studio-20", Arg.Any<CancellationToken>()).Returns(Studio);

        await CreateMiddleware().InvokeAsync(Request("studio-20.localhost"), _tenantContext, _lookup);

        _tenantContext.Received(1).SetTenant(TenantA);
        _nextCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task Unknown_subdomain_is_404_and_stops_the_pipeline()
    {
        _lookup.FindBySlugAsync("nobody", Arg.Any<CancellationToken>()).Returns((TenantInfo?)null);
        var context = Request("nobody.localhost");

        await CreateMiddleware().InvokeAsync(context, _tenantContext, _lookup);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        _tenantContext.DidNotReceive().SetTenant(Arg.Any<Guid?>());
        _nextCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task Inactive_tenant_is_404()
    {
        _lookup.FindBySlugAsync("closed", Arg.Any<CancellationToken>()).Returns(Studio with { Slug = "closed", IsActive = false });
        var context = Request("closed.localhost");

        await CreateMiddleware().InvokeAsync(context, _tenantContext, _lookup);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        _nextCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task Bare_base_domain_has_no_tenant_and_continues()
    {
        await CreateMiddleware().InvokeAsync(Request("localhost"), _tenantContext, _lookup);

        _tenantContext.DidNotReceive().SetTenant(Arg.Any<Guid?>());
        await _lookup.DidNotReceive().FindBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _nextCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task Signed_in_tenant_claim_wins_over_the_subdomain()
    {
        var claimed = Guid.CreateVersion7();

        await CreateMiddleware().InvokeAsync(Request("studio-20.localhost", claimed), _tenantContext, _lookup);

        _tenantContext.Received(1).SetTenant(claimed);
        await _lookup.DidNotReceive().FindBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _nextCalled.ShouldBeTrue();
    }
}