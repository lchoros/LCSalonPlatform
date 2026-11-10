using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Messaging;
using Salon.Services.Notifications;

namespace Salon.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers cross-slice services. Settings, localization, events and cache are added by later slices.</summary>
    public static IServiceCollection AddSalonServices(this IServiceCollection services)
    {
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.SectionName);
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }
}