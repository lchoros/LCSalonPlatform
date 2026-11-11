namespace Salon.Web.Framework.Infrastructure;

/// <summary>
/// Host-level feature flags (section "Features"). New slices ship behind a flag that defaults to off (CLAUDE.md Tier 1);
/// <c>appsettings.Development.json</c> turns them on locally. Gate a controller or action with
/// <c>[FeatureGate(nameof(FeaturesOptions.Preview))]</c>. Admin screens are guarded by <c>[RequirePermission]</c> (ADR 0009),
/// not by a flag.
/// </summary>
public sealed class FeaturesOptions
{
    public const string SectionName = "Features";

    /// <summary>Screens that are built but not yet released. Nothing uses it today; it keeps the gate exercised.</summary>
    public bool Preview { get; set; }
}