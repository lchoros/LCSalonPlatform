using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

namespace Salon.Services.Localization;

public static class LocalizationServiceCollectionExtensions
{
    /// <summary>
    /// Registers one <see cref="EmbeddedJsonResourceSource"/> per assembly, read from its embedded
    /// <c>Localization/{culture}/{Module}.json</c> files (or a single <c>Localization/{culture}.json</c>). Call it for the
    /// host and for every plugin that carries UI text; <see cref="ILocalizationService"/> merges them.
    /// </summary>
    public static IServiceCollection AddLocalizationResources(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies.Distinct())
        {
            services.AddSingleton<IResourceSource>(new EmbeddedJsonResourceSource(assembly));
        }

        return services;
    }
}