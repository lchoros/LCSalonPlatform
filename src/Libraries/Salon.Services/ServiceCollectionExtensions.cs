using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Messaging;
using Salon.Core.Tasks;
using Salon.Core.Tenancy;
using Salon.Services.Caching;
using Salon.Services.Notifications;
using Salon.Services.Tasks;
using Salon.Services.Tenancy;

namespace Salon.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers cross-slice services: clock, tenant context, cache, schedule tasks, email. Settings, localization and events come later.</summary>
    public static IServiceCollection AddSalonServices(this IServiceCollection services)
    {
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<IMutableTenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddMemoryCache();
        services.AddSingleton<IStaticCacheManager, MemoryCacheManager>();
        services.AddScoped<ITenantLookup, TenantLookup>();
        services.AddSingleton<ITenantScopeRunner, TenantScopeRunner>();

        services.AddScoped<ScheduleTaskRunner>();
        services.AddScoped<IScheduleTask, HeartbeatTask>();

        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.SectionName);
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }
}