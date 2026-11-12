using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Import;
using Salon.Core.Plugins;
using Salon.Plugin.Import.Parsers;

namespace Salon.Plugin.Import;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the CSV client import plugin (ADR 0019) with its Booksy and Versum parsers.</summary>
    public static IServiceCollection AddCsvClientImport(this IServiceCollection services)
    {
        services.AddSingleton<IClientImportParser, BooksyCsvParser>();
        services.AddSingleton<IClientImportParser, VersumCsvParser>();
        services.AddSingleton<CsvClientImportProvider>();
        services.AddSingleton<IClientImportProvider>(sp => sp.GetRequiredService<CsvClientImportProvider>());
        services.AddSingleton<IPlugin>(sp => sp.GetRequiredService<CsvClientImportProvider>());
        return services;
    }
}
