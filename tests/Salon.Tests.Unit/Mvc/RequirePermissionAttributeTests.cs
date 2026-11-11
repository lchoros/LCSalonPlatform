using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Web.Framework.Mvc;

namespace Salon.Tests.Unit.Mvc;

public class RequirePermissionAttributeTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();

    [Fact]
    public async Task Anonymous_on_a_salon_host_is_challenged()
    {
        var context = Context(user: null, TenantId);

        await new RequirePermissionAttribute(Permissions.CatalogManage).OnAuthorizationAsync(context);

        context.Result.ShouldBeOfType<ChallengeResult>();
    }

    [Fact]
    public async Task Anonymous_on_the_platform_domain_is_404_because_there_is_no_login_there()
    {
        var context = Context(user: null, tenantId: null);

        await new RequirePermissionAttribute(Permissions.CatalogManage).OnAuthorizationAsync(context);

        context.Result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Signed_in_without_the_permission_is_forbidden()
    {
        var context = Context(Principal(TenantId, Permissions.BookingsManage), TenantId);

        await new RequirePermissionAttribute(Permissions.CatalogManage).OnAuthorizationAsync(context);

        context.Result.ShouldBeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Signed_in_with_the_permission_passes()
    {
        var context = Context(Principal(TenantId, Permissions.BookingsManage, Permissions.CatalogManage), TenantId);

        await new RequirePermissionAttribute(Permissions.CatalogManage).OnAuthorizationAsync(context);

        context.Result.ShouldBeNull();
    }

    [Fact]
    public async Task A_cookie_of_another_tenant_counts_as_anonymous()
    {
        var context = Context(Principal(Guid.CreateVersion7(), Permissions.CatalogManage), TenantId);

        await new RequirePermissionAttribute(Permissions.CatalogManage).OnAuthorizationAsync(context);

        context.Result.ShouldBeOfType<ChallengeResult>();
    }

    [Fact]
    public async Task An_earlier_filter_decision_is_left_alone()
    {
        var context = Context(user: null, TenantId);
        context.Result = new StatusCodeResult(418);

        await new RequirePermissionAttribute(Permissions.CatalogManage).OnAuthorizationAsync(context);

        context.Result.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(418);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Catalog.Delete")]
    [InlineData("catalog.manage")]
    public void An_unknown_permission_name_fails_at_construction(string permission)
    {
        Should.Throw<ArgumentException>(() => new RequirePermissionAttribute(permission));
    }

    [Fact]
    public void Every_catalogued_permission_is_accepted()
    {
        foreach (var permission in Permissions.All)
        {
            new RequirePermissionAttribute(permission).Permission.ShouldBe(permission);
        }
    }

    private static ClaimsPrincipal Principal(Guid tenantId, params string[] permissions)
    {
        var claims = new List<Claim> { new(UserClaimTypes.Subject, Guid.CreateVersion7().ToString()), new(UserClaimTypes.TenantId, tenantId.ToString()) };
        claims.AddRange(permissions.Select(p => new Claim(UserClaimTypes.Permission, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static AuthorizationFilterContext Context(ClaimsPrincipal? user, Guid? tenantId)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(tenantContext).BuildServiceProvider(),
        };
        if (user is not null)
        {
            httpContext.User = user;
        }

        return new AuthorizationFilterContext(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()), []);
    }
}