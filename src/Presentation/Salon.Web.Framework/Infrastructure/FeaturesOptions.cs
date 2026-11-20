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

    /// <summary>Client CSV import from Booksy/Versum at /admin/clients/import (ADR 0019).</summary>
    public bool ClientImport { get; set; }

    /// <summary>The public sign-up page at /signup on the base domain (ADR 0075): off until the owner opens the door.</summary>
    public bool SignUp { get; set; }

    /// <summary>
    /// Stripe in test mode for the deposit of an online booking (ADR 0082, Tier 2): the pay step after the code, its
    /// return address and the webhook. Off everywhere unless the owner turns it on; the Stripe plugin reads the same key.
    /// </summary>
    public bool StripeDeposits { get; set; }
}