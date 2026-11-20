using Salon.Core.Plugins;

namespace Salon.Plugin.Payments.Stripe;

/// <summary>
/// Stripe in test mode for one thing: the deposit of an online booking (ADR 0082). It does not replace the manual
/// payment provider, which stays the way money taken on the salon's floor is recorded. Does nothing unless
/// <c>Features:StripeDeposits</c> is on and a test-mode key is configured.
/// </summary>
public sealed class StripePaymentsPlugin : IPlugin
{
    public const string PluginSystemName = "Payments.Stripe";

    public string SystemName => PluginSystemName;

    public string DisplayNameKey => "Plugins.Payments.Stripe.DisplayName";
}