using NetArchTest.Rules;

namespace Salon.Tests.Architecture;

/// <summary>CLAUDE.md rule 3: a slice never references another slice. Cross-module work goes through events or Salon.Services.</summary>
public class SliceIsolationTests
{
    private const string FeaturesPrefix = "Salon.Web.Features.";

    [Fact]
    public void A_slice_never_references_another_slice()
    {
        var slices = Assemblies.Web.GetTypes()
            .Select(t => t.Namespace)
            .Where(ns => ns is not null && ns.StartsWith(FeaturesPrefix, StringComparison.Ordinal))
            .Select(ns => SliceNamespace(ns!))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var slice in slices)
        {
            var otherSlices = slices.Where(s => !string.Equals(s, slice, StringComparison.Ordinal)).ToArray();
            if (otherSlices.Length == 0)
            {
                continue;
            }

            var result = Types.InAssembly(Assemblies.Web)
                .That().ResideInNamespaceStartingWith(slice)
                .ShouldNot().HaveDependencyOnAny(otherSlices)
                .GetResult();

            result.IsSuccessful.ShouldBeTrue($"{slice}: {LayerDependencyTests.Describe(result)}");
        }
    }

    /// <summary>Salon.Web.Features.Catalog.CreateService.Anything → Salon.Web.Features.Catalog.CreateService</summary>
    private static string SliceNamespace(string ns)
    {
        var parts = ns.Split('.');
        return string.Join('.', parts.Take(5));
    }
}