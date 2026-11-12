using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Salon.Services.Notifications;
using Salon.Web.Framework.Booking;

namespace Salon.Web.Framework.Tenancy;

/// <summary>
/// <see cref="IPublicUrlBuilder"/> over the host's tenancy options (ADR 0014): <c>{scheme}://{slug}.{first base domain}[:port]{path}</c>.
/// The scheme is <see cref="TenancyOptions.PublicScheme"/> when set, else https — or http in Development, where there is
/// no certificate for <c>*.localhost</c>. Links are built from configuration, never from the incoming request's Host
/// header, so a spoofed header cannot steer a client to another site.
/// </summary>
internal sealed class PublicUrlBuilder(IOptions<TenancyOptions> options, IHostEnvironment environment) : IPublicUrlBuilder
{
    public string BookingUrl(string tenantSlug) => Build(tenantSlug, PublicBookingDefaults.BookPath);

    public string CancelBookingUrl(string tenantSlug, string publicToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicToken);
        return Build(tenantSlug, PublicBookingDefaults.CancelPath(publicToken));
    }

    private string Build(string tenantSlug, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);
        var settings = options.Value;
        var baseDomain = settings.BaseDomains.FirstOrDefault() ?? TenancyOptions.DefaultBaseDomain;
        var scheme = settings.PublicScheme ?? (environment.IsDevelopment() ? Uri.UriSchemeHttp : Uri.UriSchemeHttps);
        var builder = new UriBuilder(scheme, $"{tenantSlug.ToLowerInvariant()}.{baseDomain}") { Path = path };
        if (settings.PublicPort is { } port)
        {
            builder.Port = port;
        }

        return builder.Uri.ToString();
    }
}
