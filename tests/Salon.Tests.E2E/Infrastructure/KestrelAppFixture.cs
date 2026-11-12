using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

using NodaTime;

using Salon.Core.Catalog;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Identity;
using Salon.Services.Seeding;

namespace Salon.Tests.E2E.Infrastructure;

/// <summary>Starts Salon.Web on a real Kestrel port and a headless Chromium so Playwright can drive the pages.</summary>
public sealed class KestrelAppFixture : IAsyncLifetime, IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new HostFactory();
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;

    /// <summary>The platform site, e.g. http://127.0.0.1:54321.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>Screenshots land in tests/Salon.Tests.E2E/screenshots so CI can upload them and PRs can attach them.</summary>
    public static string ScreenshotDirectory { get; } =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "screenshots"));

    /// <summary>
    /// The same server addressed as a salon: "{slug}.localhost" resolves to the loopback address in Chromium (and in
    /// Windows and glibc resolvers), so the tenant middleware sees the subdomain exactly as in production.
    /// </summary>
    public string TenantUrl(string slug) => new UriBuilder(BaseUrl) { Host = $"{slug}.localhost" }.Uri.ToString().TrimEnd('/');

    public async Task InitializeAsync()
    {
        // Port 0: xUnit runs test classes in parallel and each class owns a fixture, so two hosts must not share a port.
        _factory.UseKestrel(0);
        _factory.StartServer();
        var addresses = _factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        BaseUrl = (addresses?.FirstOrDefault() ?? throw new InvalidOperationException("Kestrel did not report an address.")).TrimEnd('/');

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Database.MigrateAsync();
        }

        Directory.CreateDirectory(ScreenshotDirectory);
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    /// <summary>
    /// Makes sure the salon exists (en-GB, GBP, Europe/London unless told otherwise) and that its catalogue holds exactly
    /// <paramref name="services"/>, in the given order. Safe to call repeatedly and after an integration run left other
    /// rows behind: the tenant row is kept, its services are replaced (appointments go first, they hold the services).
    /// </summary>
    public async Task<Guid> EnsureTenantAsync(
        string slug,
        string name,
        IReadOnlyList<(string Name, int Minutes, decimal Price)> services,
        string culture = "en-GB",
        string currency = "GBP")
    {
        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Slug == slug);
            if (tenant is null)
            {
                tenant = new Tenant
                {
                    Slug = slug,
                    Name = name,
                    TimeZoneId = culture == "pl-PL" ? "Europe/Warsaw" : "Europe/London",
                    Culture = culture,
                    Currency = currency,
                    CreatedAt = SystemClock.Instance.GetCurrentInstant(),
                };
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();
            }

            tenantId = tenant.Id;
        }

        // A fresh scope with the tenant set before the connection opens, so the filter and RLS setting are both right.
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IMutableTenantContext>().SetTenant(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.Appointments.RemoveRange(await db.Appointments.ToListAsync());
            db.Services.RemoveRange(await db.Services.ToListAsync());
            var sortOrder = 0;
            foreach (var (serviceName, minutes, price) in services)
            {
                db.Services.Add(new Service
                {
                    Name = serviceName,
                    Duration = Duration.FromMinutes(minutes),
                    Price = price,
                    Currency = currency,
                    SortOrder = ++sortOrder,
                });
            }

            await db.SaveChangesAsync();
        }

        return tenantId;
    }

    /// <summary>
    /// Replaces the salon's staff with the three demo people and their rota (ADR 0010) through the real DemoSeeder, which
    /// only adds staff, clients and appointments to a salon that has none of each; the salon must be the demo slug. Safe to
    /// call repeatedly: appointments, staff and clients left by an earlier run go first, so the demo data is the same every time.
    /// </summary>
    public async Task EnsureDemoStaffAsync(Guid tenantId)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IMutableTenantContext>().SetTenant(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.Appointments.RemoveRange(await db.Appointments.ToListAsync());
            db.StaffMembers.RemoveRange(await db.StaffMembers.ToListAsync());
            db.Clients.RemoveRange(await db.Clients.ToListAsync());
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync();
        }
    }

    /// <summary>
    /// Gives the salon a login with exactly this role and password. Any earlier user with the email (left by an
    /// integration run, possibly locked out) is removed first, so the test never depends on what ran before.
    /// </summary>
    public async Task EnsureUserAsync(Guid tenantId, string email, string displayName, Role role, string password)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMutableTenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var normalized = email.ToLowerInvariant();
        db.Users.RemoveRange(await db.Users.Where(u => u.Email == normalized).ToListAsync());
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<IUserService>().CreateAsync(email, displayName, password, role);
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
        await _factory.DisposeAsync();
    }

    async ValueTask IAsyncDisposable.DisposeAsync() => await DisposeAsync();

    private sealed class HostFactory : WebApplicationFactory<Program>
    {
        public HostFactory()
        {
            Environment.SetEnvironmentVariable(
                "ConnectionStrings__Default",
                Environment.GetEnvironmentVariable("ConnectionStrings__Test")
                ?? "Host=localhost;Port=5432;Database=salon_test;Username=postgres;Password=postgres");
            Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "false");
            Environment.SetEnvironmentVariable("Jobs__Enabled", "false");
            // Slices behind a flag (CLAUDE.md Tier 1) are on in the E2E host.
            Environment.SetEnvironmentVariable("Features__ClientImport", "true");
        }

        // UseStaticWebAssets maps the build-time content roots (including the pre-compressed css/app.css.gz under obj/);
        // without it MapStaticAssets answers a gzip request with an empty body and the browser renders an unstyled page.
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing").UseStaticWebAssets();
    }
}