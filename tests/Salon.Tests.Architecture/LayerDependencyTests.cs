using NetArchTest.Rules;

namespace Salon.Tests.Architecture;

/// <summary>CLAUDE.md rule 1: dependencies only point down. Web → Web.Framework → Services → Data → Core.</summary>
public class LayerDependencyTests
{
    [Fact]
    public void Core_has_no_infrastructure_or_upper_layer_dependencies()
    {
        var result = Types.InAssembly(Assemblies.Core)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Salon.Data", "Salon.Services", "Salon.Web", "Salon.Plugin",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "Hangfire", "MailKit")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Data_does_not_depend_on_services_or_presentation()
    {
        var result = Types.InAssembly(Assemblies.Data)
            .ShouldNot()
            .HaveDependencyOnAny("Salon.Services", "Salon.Web", "Salon.Plugin", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Services_do_not_depend_on_presentation()
    {
        var result = Types.InAssembly(Assemblies.Services)
            .ShouldNot()
            .HaveDependencyOnAny("Salon.Web", "Salon.Plugin", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Web_framework_does_not_depend_on_the_host_or_plugins()
    {
        var result = Types.InAssembly(Assemblies.WebFramework)
            .ShouldNot()
            .HaveDependencyOnAny("Salon.Web.Features", "Salon.Web.Areas", "Salon.Plugin")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));

        var hostControllers = Types.InAssembly(Assemblies.WebFramework)
            .ShouldNot()
            .HaveDependencyOn("Salon.Web.Controllers")
            .GetResult();

        hostControllers.IsSuccessful.ShouldBeTrue(Describe(hostControllers));
    }

    [Fact]
    public void Plugins_reference_services_and_core_only()
    {
        foreach (var plugin in Assemblies.Plugins)
        {
            var result = Types.InAssembly(plugin)
                .ShouldNot()
                .HaveDependencyOnAny("Salon.Data", "Salon.Web", "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore")
                .GetResult();

            result.IsSuccessful.ShouldBeTrue($"{plugin.GetName().Name}: {Describe(result)}");
        }
    }

    internal static string Describe(TestResult result)
        => result.IsSuccessful ? "ok" : "Violations: " + string.Join(", ", result.FailingTypeNames ?? []);
}