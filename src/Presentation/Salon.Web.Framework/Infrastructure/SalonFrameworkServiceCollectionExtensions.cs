using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Salon.Data;
using Salon.Services;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Framework.Infrastructure;

public static class SalonFrameworkServiceCollectionExtensions
{
    /// <summary>Wires the data layer, cross-slice services and MVC with the Features/ view convention.</summary>
    public static IServiceCollection AddSalonFramework(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<SalonDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseNodaTime())
                   .UseSnakeCaseNamingConvention());

        services.AddSalonServices();
        services.AddHttpContextAccessor();

        services.AddControllersWithViews();
        services.Configure<RazorViewEngineOptions>(options =>
            options.ViewLocationExpanders.Add(new FeatureViewLocationExpander()));

        return services;
    }
}