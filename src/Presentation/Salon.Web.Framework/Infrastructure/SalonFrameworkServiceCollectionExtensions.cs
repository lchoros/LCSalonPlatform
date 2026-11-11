using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

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
        EnsureTenantSafeConnectionString(connectionString);

        services.AddSalonServices();
        services.AddOptions<TenancyOptions>().BindConfiguration(TenancyOptions.SectionName)
            .PostConfigure(o =>
            {
                if (o.BaseDomains.Count == 0)
                {
                    o.BaseDomains.Add(TenancyOptions.DefaultBaseDomain);
                }
            });
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

    /// <summary>
    /// Tenant isolation (ADR 0002) relies on Npgsql resetting the session setting app.tenant_id when a pooled connection is
    /// returned. "No Reset On Close" and "Multiplexing" would let tenant A's setting leak into tenant B's request, so refuse them.
    /// </summary>
    internal static void EnsureTenantSafeConnectionString(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (builder.NoResetOnClose || builder.Multiplexing)
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default must not set 'No Reset On Close' or 'Multiplexing'; tenant isolation depends on the pool reset (ADR 0002).");
        }
    }
}