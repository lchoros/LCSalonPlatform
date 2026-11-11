using Salon.Core.Plugins;

namespace Salon.Tests.Architecture;

/// <summary>Kickoff rule: plugins are IPlugin + plugin.json, compiled in. A plugin assembly without the descriptor fails here, not at startup.</summary>
public class PluginPackagingTests
{
    [Fact]
    public void Every_plugin_assembly_embeds_a_plugin_json_and_exposes_exactly_one_IPlugin()
    {
        foreach (var assembly in Assemblies.Plugins)
        {
            assembly.GetManifestResourceNames()
                .ShouldContain(n => n.EndsWith("plugin.json", StringComparison.OrdinalIgnoreCase), $"{assembly.GetName().Name} has no plugin.json");

            assembly.GetTypes()
                .Count(t => t.IsClass && !t.IsAbstract && typeof(IPlugin).IsAssignableFrom(t))
                .ShouldBe(1, $"{assembly.GetName().Name} must expose exactly one IPlugin");
        }
    }
}