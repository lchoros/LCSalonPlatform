using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Unicode;

using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Salon.Data;
using Salon.Data.Tenancy;
using Salon.Services;
using Salon.Services.Clients;
using Salon.Services.Events;
using Salon.Services.Localization;
using Salon.Services.Security;
using Salon.Web.Framework.Mvc;
using Salon.Web.Framework.Pipeline;
using Salon.Web.Framework.Security;
using Salon.Web.Framework.Tenancy;

namespace Salon.Web.Framework.Infrastructure;

public static class SalonFrameworkServiceCollectionExtensions
{
    /// <summary>
    /// Wires the data layer, cross-slice services, the request pipeline, the event outbox and MVC with the Features/
    /// view convention. <paramref name="sliceAssemblies"/> are scanned for request handlers, validators, events and
    /// consumers and read for embedded localization resources (pass the host assembly).
    /// </summary>
    public static IServiceCollection AddSalonFramework(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] sliceAssemblies)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        EnsureTenantSafeConnectionString(connectionString);

        services.AddSalonServices();
        services.AddLocalizationResources(sliceAssemblies);

        // Polish letters (ą, ś, ł, ...) and typographic punctuation ship as UTF-8 instead of &#x105; entities.
        services.AddWebEncoders(o => o.TextEncoderSettings = new TextEncoderSettings(
            UnicodeRanges.BasicLatin,
            UnicodeRanges.Latin1Supplement,
            UnicodeRanges.LatinExtendedA,
            UnicodeRanges.LatinExtendedB,
            UnicodeRanges.GeneralPunctuation));
        services.AddOptions<FeaturesOptions>().BindConfiguration(FeaturesOptions.SectionName);
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
        services.AddSalonPipeline(sliceAssemblies);
        services.AddSalonEvents(sliceAssemblies);

        // Field encryption for client health notes (ADR 0011) rides on the Data Protection key ring that cookies and
        // anti-forgery already use, under its own purpose string; the client service needs it, so both register here.
        services.AddDataProtection();
        services.AddSingleton<IFieldEncryptor, DataProtectionFieldEncryptor>();
        services.AddSalonClients();

        // Validation is FluentValidation's job (ADR 0003) with localized messages; MVC must not add its own English
        // "The X field is required." for non-nullable reference properties before the pipeline runs.
        services.AddControllersWithViews(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
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