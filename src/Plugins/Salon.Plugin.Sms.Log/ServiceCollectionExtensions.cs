using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Messaging;
using Salon.Core.Plugins;

namespace Salon.Plugin.Sms.Log;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLogSmsProvider(this IServiceCollection services)
    {
        services.AddSingleton<LogSmsProvider>();
        services.AddSingleton<ISmsSender>(sp => sp.GetRequiredService<LogSmsProvider>());
        services.AddSingleton<IPlugin>(sp => sp.GetRequiredService<LogSmsProvider>());
        return services;
    }
}