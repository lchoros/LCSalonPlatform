using Microsoft.Extensions.Logging.Abstractions;

using Salon.Core.Payments;
using Salon.Core.Plugins;
using Salon.Plugin.Payments.Manual;
using Salon.Plugin.Sms.Log;
using Salon.Web.Framework.Plugins;

namespace Salon.Tests.Unit.Plugins;

public class PluginManagerTests
{
    private static readonly ManualPaymentProvider Manual = new(NullLogger<ManualPaymentProvider>.Instance);
    private static readonly LogSmsProvider LogSms = new(NSubstitute.Substitute.For<Salon.Core.Messaging.IEmailSender>(), NullLogger<LogSmsProvider>.Instance);

    [Fact]
    public void Loads_descriptors_of_the_shipped_plugins()
    {
        var manager = new PluginManager([Manual, LogSms], NullLogger<PluginManager>.Instance);

        manager.Plugins.Select(p => p.Descriptor.SystemName).ShouldBe(["Payments.Manual", "Sms.Log"]);
        manager.Plugins.ShouldAllBe(p => !string.IsNullOrWhiteSpace(p.Descriptor.Version) && !string.IsNullOrWhiteSpace(p.Descriptor.Group));
        manager.FindBySystemName("Sms.Log")!.Instance.ShouldBeSameAs(LogSms);
        manager.FindBySystemName("Nope").ShouldBeNull();
    }

    [Fact]
    public void Filters_plugins_by_contract()
    {
        var manager = new PluginManager([Manual, LogSms], NullLogger<PluginManager>.Instance);

        manager.GetPlugins<IPaymentProvider>().ShouldBe([Manual]);
        manager.GetPlugins<IWidgetPlugin>().ShouldBeEmpty();
    }

    [Fact]
    public void Rejects_a_plugin_whose_system_name_differs_from_its_descriptor()
    {
        var descriptor = new PluginDescriptor("Payments.Other", "key", "Payments", "x", "1.0.0", "d");

        Should.Throw<PluginLoadException>(() => PluginDescriptorReader.Validate(Manual, descriptor))
            .Message.ShouldContain("Payments.Other");
    }

    [Fact]
    public void Rejects_a_plugin_whose_assembly_has_no_descriptor()
    {
        Should.Throw<PluginLoadException>(() => new PluginManager([new ImpostorPlugin()], NullLogger<PluginManager>.Instance))
            .Message.ShouldContain("plugin.json");
    }

    [Fact]
    public void Rejects_duplicate_system_names()
    {
        Should.Throw<PluginLoadException>(() => new PluginManager([Manual, Manual], NullLogger<PluginManager>.Instance));
    }

    [Fact]
    public void Rejects_an_assembly_without_plugin_json()
    {
        Should.Throw<PluginLoadException>(() => PluginDescriptorReader.Read(typeof(PluginManagerTests).Assembly));
    }

    /// <summary>Defined in the test assembly, which has no plugin.json.</summary>
    private sealed class ImpostorPlugin : IPlugin
    {
        public string SystemName => "Impostor";

        public string DisplayNameKey => "Plugins.Impostor";
    }
}