using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Messaging;
using Salon.Core.Tasks;
using Salon.Core.Tenancy;
using Salon.Services.Caching;
using Salon.Services.Localization;
using Salon.Services.Notifications;
using Salon.Services.Seeding;
using Salon.Services.Settings;
using Salon.Services.Tasks;
using Salon.Services.Tenancy;

namespace Salon.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers cross-slice services: clock, tenant context, cache, settings, schedule tasks, localization, email.
    /// Resource sources for localization are added per assembly with <see cref="LocalizationServiceCollectionExtensions.AddLocalizationResources"/>.
    /// Events and the outbox are added per assembly with <see cref="Events.EventsServiceCollectionExtensions.AddSalonEvents"/>.
    /// </summary>
    public static IServiceCollection AddSalonServices(this IServiceCollection services)
    {
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<ILocalizationService, LocalizationService>();

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<IMutableTenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddMemoryCache();
        services.AddSingleton<IStaticCacheManager, MemoryCacheManager>();
        services.AddScoped<ITenantLookup, TenantLookup>();
        services.AddSingleton<ITenantScopeRunner, TenantScopeRunner>();

        services.AddScoped<ScheduleTaskRunner>();
        services.AddScoped<IScheduleTask, HeartbeatTask>();
        services.AddScoped<ISettingService, SettingService>();
        services.AddOptions<DemoOptions>().BindConfiguration(DemoOptions.SectionName);
        services.AddScoped<DemoSeeder>();

        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.SectionName);
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }
}