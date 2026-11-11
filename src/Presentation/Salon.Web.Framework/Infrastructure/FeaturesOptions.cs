namespace Salon.Web.Framework.Infrastructure;

/// <summary>
/// Host-level feature flags (section "Features"). New slices ship behind a flag that defaults to off (CLAUDE.md Tier 1);
/// <c>appsettings.Development.json</c> turns them on locally. Gate a controller or action with
/// <c>[FeatureGate(nameof(FeaturesOptions.CatalogAdmin))]</c>.
/// </summary>
public sealed class FeaturesOptions
{
    public const string SectionName = "Features";

    /// <summary>The service catalogue admin screens (Features/Catalog). Off until permissions exist; the screens are 404 without it.</summary>
    public bool CatalogAdmin { get; set; }
}