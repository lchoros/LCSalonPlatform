using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Salon.Data;
using Salon.Data.Tenancy;
using Salon.Services;
using Salon.Web.Framework.Mvc;
using Salon.Web.Framework.Tenancy;

namespace Salon.Web.Framework.Infrastructure;

public static class SalonFrameworkServiceCollectionExtensions
{
    /// <summary>Wires the data layer, cross-slice services and MVC with the Features/ view convention.</summary>
    public static IServiceCollection AddSalonFramework(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddSalonServices();
        services.AddOptions<TenancyOptions>().BindConfiguration(TenancyOptions.SectionName);
        services.AddScoped<TenantConnectionInterceptor>();
        services.AddDbContext<SalonDbContext>((provider, options) =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseNodaTime())
                   .UseSnakeCaseNamingConvention()
                   .AddInterceptors(provider.GetRequiredService<TenantConnectionInterceptor>()));

        services.AddHttpContextAccessor();

        services.AddControllersWithViews();
        services.Configure<RazorViewEngineOptions>(options =>
            options.ViewLocationExpanders.Add(new FeatureViewLocationExpander()));

        return services;
    }
}