using Microsoft.Extensions.DependencyInjection;
using Salon.Tests.Integration.Infrastructure;
using Salon.Web.Framework.Plugins;

namespace Salon.Tests.Integration.Plugins;

[Collection(SharedDatabase.Name)]
public class PluginStartupTests(SalonWebApplicationFactory factory)
{
    [Fact]
    public void Host_loads_the_phase_1_plugins_with_descriptors()
    {
        var manager = factory.Services.GetRequiredService<IPluginManager>();

        manager.Plugins.Select(p => p.Descriptor.SystemName).Order().ShouldBe(["Import.Csv", "Payments.Manual", "Sms.Log"]);
        manager.Plugins.ShouldAllBe(p => p.Descriptor.Version == "0.1.0");
    }
}