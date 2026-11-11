using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Salon.Web.Framework.Infrastructure;
using Salon.Web.Framework.Mvc;

namespace Salon.Tests.Unit.Mvc;

public class FeatureGateAttributeTests
{
    [Fact]
    public void Flag_off_short_circuits_with_404()
    {
        var context = Context(new FeaturesOptions { Preview = false });

        new FeatureGateAttribute(nameof(FeaturesOptions.Preview)).OnActionExecuting(context);

        context.Result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public void Flag_on_lets_the_action_run()
    {
        var context = Context(new FeaturesOptions { Preview = true });

        new FeatureGateAttribute(nameof(FeaturesOptions.Preview)).OnActionExecuting(context);

        context.Result.ShouldBeNull();
    }

    [Fact]
    public void Flags_default_to_off()
    {
        var context = Context(new FeaturesOptions());

        new FeatureGateAttribute(nameof(FeaturesOptions.Preview)).OnActionExecuting(context);

        context.Result.ShouldBeOfType<NotFoundResult>();
    }

    [Theory]
    [InlineData("NoSuchFlag")]
    [InlineData("")]
    [InlineData("SectionName")]
    public void An_unknown_or_non_bool_flag_fails_at_construction(string flag)
    {
        Should.Throw<ArgumentException>(() => new FeatureGateAttribute(flag));
    }

    internal static ActionExecutingContext Context(FeaturesOptions options)
    {
        var services = new ServiceCollection();
        services.AddOptions<FeaturesOptions>().Configure(o => o.Preview = options.Preview);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        return new ActionExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            [],
            new Dictionary<string, object?>(),
            controller: new object());
    }
}