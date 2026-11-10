using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Payments;
using Salon.Core.Plugins;

namespace Salon.Plugin.Payments.Manual;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddManualPaymentProvider(this IServiceCollection services)
    {
        services.AddSingleton<ManualPaymentProvider>();
        services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<ManualPaymentProvider>());
        services.AddSingleton<IPlugin>(sp => sp.GetRequiredService<ManualPaymentProvider>());
        return services;
    }
}