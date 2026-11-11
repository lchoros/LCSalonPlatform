using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Salon.Core.Tenancy;
using Salon.Web.Framework.Mvc;

namespace Salon.Tests.Unit.Mvc;

public class RequireTenantAttributeTests
{
    [Fact]
    public void No_tenant_in_scope_short_circuits_with_404()
    {
        var context = Context(tenantId: null);

        new RequireTenantAttribute().OnActionExecuting(context);

        context.Result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public void A_tenant_in_scope_lets_the_action_run()
    {
        var context = Context(Guid.CreateVersion7());

        new RequireTenantAttribute().OnActionExecuting(context);

        context.Result.ShouldBeNull();
    }

    private static ActionExecutingContext Context(Guid? tenantId)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(tenantContext).BuildServiceProvider(),
        };
        return new ActionExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            [],
            new Dictionary<string, object?>(),
            controller: new object());
    }
}