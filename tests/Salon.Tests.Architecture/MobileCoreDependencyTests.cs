using NetArchTest.Rules;

namespace Salon.Tests.Architecture;

/// <summary>
/// ADR 0097: the client app's logic is a plain library over the shared shapes. Salon.Mobile.Core may reference
/// Salon.Api.Contracts and nothing else of ours, and no server or UI framework, so the app never drags the web host's
/// libraries onto a phone and the screens stay MAUI's business.
/// </summary>
public class MobileCoreDependencyTests
{
    [Fact]
    public void Mobile_core_references_only_the_api_contracts_of_our_assemblies()
    {
        var ours = Assemblies.MobileCore.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith("Salon.", StringComparison.Ordinal))
            .ToList();

        ours.ShouldBe(["Salon.Api.Contracts"]);
    }

    [Fact]
    public void Mobile_core_has_no_server_or_ui_framework_dependencies()
    {
        var result = Types.InAssembly(Assemblies.MobileCore)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Salon.Core", "Salon.Data", "Salon.Services", "Salon.Web", "Salon.Plugin",
                "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Microsoft.Maui", "Npgsql", "Hangfire", "NodaTime")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(LayerDependencyTests.Describe(result));
    }

    [Fact]
    public void The_api_contracts_reference_nothing_of_ours()
    {
        Assemblies.ApiContracts.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith("Salon.", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }
}