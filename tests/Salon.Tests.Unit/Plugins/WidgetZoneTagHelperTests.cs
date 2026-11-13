using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using NSubstitute;
using Salon.Core.Plugins;
using Salon.Web.Framework.Plugins;
using Salon.Web.Framework.TagHelpers;

namespace Salon.Tests.Unit.Plugins;

public class WidgetZoneTagHelperTests
{
    private readonly IPluginManager _plugins = Substitute.For<IPluginManager>();
    private readonly IViewComponentHelper _components = Substitute.For<IViewComponentHelper, IViewContextAware>();

    private static (TagHelperContext Context, TagHelperOutput Output) Tag()
    {
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("widget-zone", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        return (context, output);
    }

    [Fact]
    public async Task Renders_nothing_when_no_plugin_claims_the_zone()
    {
        _plugins.GetPlugins<IWidgetPlugin>().Returns([]);
        var helper = new WidgetZoneTagHelper(_plugins, _components) { Name = WidgetZones.AdminDashboardTop, ViewContext = new ViewContext() };
        var (context, output) = Tag();

        await helper.ProcessAsync(context, output);

        output.TagName.ShouldBeNull();
        output.Content.GetContent().ShouldBeEmpty();
        output.PostContent.GetContent().ShouldBeEmpty();
    }

    [Fact]
    public async Task Renders_each_claiming_plugins_view_component_in_order()
    {
        var first = Widget("First", WidgetZones.AdminDashboardTop);
        var other = Widget("Other", WidgetZones.BookingConfirmationBottom);
        var second = Widget("Second", WidgetZones.AdminDashboardTop);
        _plugins.GetPlugins<IWidgetPlugin>().Returns([first, other, second]);
        _components.InvokeAsync(Arg.Any<string>(), Arg.Any<object>())
            .Returns(call => new HtmlString($"<div>{call.ArgAt<string>(0)}</div>"));
        var helper = new WidgetZoneTagHelper(_plugins, _components) { Name = WidgetZones.AdminDashboardTop, ViewContext = new ViewContext() };
        var (context, output) = Tag();

        await helper.ProcessAsync(context, output);

        output.Content.GetContent().ShouldBe("<div>FirstComponent</div><div>SecondComponent</div>");
        ((IViewContextAware)_components).Received(1).Contextualize(Arg.Any<ViewContext>());
    }

    private static IWidgetPlugin Widget(string name, string zone)
    {
        var widget = Substitute.For<IWidgetPlugin>();
        widget.SystemName.Returns(name);
        widget.WidgetZones.Returns([zone]);
        widget.GetWidgetViewComponentName(zone).Returns(name + "Component");
        return widget;
    }
}