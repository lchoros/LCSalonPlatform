using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Salon.Core.Payments;
using Salon.Core.Plugins;

namespace Salon.Plugin.Payments.Stripe;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Stripe as the host's <see cref="IOnlineDepositCheckout"/> (ADR 0082). Reads sections "Stripe" (the
    /// keys) and "Features" (the switch). The salon's <see cref="IPaymentProvider"/> is left alone. Test hosts put a
    /// fake in place of <see cref="IStripeGateway"/>, so no automated test reaches Stripe.
    /// </summary>
    public static IServiceCollection AddStripeDeposits(this IServiceCollection services)
    {
        services.AddOptions<StripeOptions>().BindConfiguration(StripeOptions.SectionName);
        services.AddOptions<StripeFeatureOptions>().BindConfiguration(StripeFeatureOptions.SectionName);
        services.TryAddSingleton<IStripeGateway, StripeNetGateway>();
        services.RemoveAll<IOnlineDepositCheckout>();
        services.AddSingleton<IOnlineDepositCheckout, StripeDepositCheckout>();
        services.AddSingleton<StripePaymentsPlugin>();
        services.AddSingleton<IPlugin>(sp => sp.GetRequiredService<StripePaymentsPlugin>());
        return services;
    }
}