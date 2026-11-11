using System.Reflection;

using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Salon.Data;
using Salon.Data.Tenancy;
using Salon.Services;
using Salon.Web.Framework.Mvc;
using Salon.Web.Framework.Pipeline;
using Salon.Web.Framework.Tenancy;

namespace Salon.Web.Framework.Infrastructure;

public static class SalonFrameworkServiceCollectionExtensions
{
    /// <summary>
    /// Wires the data layer, cross-slice services, the request pipeline and MVC with the Features/ view convention.
    /// <paramref name="sliceAssemblies"/> are scanned for request handlers and validators (pass the host assembly).
    /// </summary>
    public static IServiceCollection AddSalonFramework(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] sliceAssemblies)
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
        services.AddSalonPipeline(sliceAssemblies);

        services.AddControllersWithViews();
        services.Configure<RazorViewEngineOptions>(options =>
            options.ViewLocationExpanders.Add(new FeatureViewLocationExpander()));

        return services;
    }
}