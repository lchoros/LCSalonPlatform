using Microsoft.Extensions.DependencyInjection;

namespace Salon.Services.Clients;

public static class ClientsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the client service (ADR 0011). It needs an <see cref="Security.IFieldEncryptor"/>, which the service layer
    /// cannot provide itself (Data Protection ships with ASP.NET Core), so the host calls this from <c>AddSalonFramework</c>
    /// right after registering the encryptor; it is not part of <c>AddSalonServices</c>.
    /// </summary>
    public static IServiceCollection AddSalonClients(this IServiceCollection services)
    {
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<Import.IClientImportService, Import.ClientImportService>();
        return services;
    }
}