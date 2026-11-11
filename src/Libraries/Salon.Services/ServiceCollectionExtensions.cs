using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Messaging;
using Salon.Core.Tenancy;
using Salon.Services.Notifications;
using Salon.Services.Tenancy;

namespace Salon.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers cross-slice services. Settings, localization, events and cache are added by later slices.</summary>
    public static IServiceCollection AddSalonServices(this IServiceCollection services)
    {
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<IMutableTenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.SectionName);
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }
}