using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NodaTime;

using Salon.Core.Catalog;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Identity;
using Salon.Services.Tenancy;

namespace Salon.Services.Seeding;

/// <summary>
/// Creates the demo salon "studio-20" (Studio 20, Europe/London, en-GB, GBP) with six services and, when
/// <c>Demo:OwnerPassword</c> is set, an Owner and a Receptionist login, so a fresh local database has something to show
/// at studio-20.localhost. Runs when <c>Database:SeedDemo</c> is true (Development only) and does nothing when the tenant
/// already exists, so restarting the app never duplicates or resets data. Staff are not seeded: there is no Staff entity before M1.
/// </summary>
public sealed class DemoSeeder(
    IServiceScopeFactory scopeFactory,
    ITenantScopeRunner tenantScopes,
    IClock clock,
    IOptions<DemoOptions> demoOptions,
    ILogger<DemoSeeder> logger)
{
    public const string Slug = "studio-20";
    public const string OwnerEmail = "owner@studio-20.local";
    public const string ReceptionistEmail = "desk@studio-20.local";

    private static readonly (string Name, string Description, int Minutes, decimal Price)[] Services =
    [
        ("Cut & blow-dry", "Consultation, wash, cut and finish.", 45, 45m),
        ("Colour (roots)", "Regrowth tint up to 2 cm, blow-dry included.", 90, 65m),
        ("Balayage", "Hand-painted lightening, toner and blow-dry.", 150, 140m),
        ("Beard trim", "Shape, trim and hot-towel finish.", 20, 15m),
        ("Manicure", "File, cuticle care and polish.", 45, 28m),
        ("Pedicure", "Soak, file, cuticle care and polish.", 60, 35m),
    ];

    /// <summary>Returns true when the tenant was created on this call, false when it already existed.</summary>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        Guid tenantId;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            if (await db.Tenants.AnyAsync(t => t.Slug == Slug, cancellationToken).ConfigureAwait(false))
            {
                logger.LogDebug("Demo tenant {Slug} already exists; nothing to seed", Slug);
                return false;
            }

            var tenant = new Tenant
            {
                Slug = Slug,
                Name = "Studio 20",
                TimeZoneId = "Europe/London",
                Culture = "en-GB",
                Currency = "GBP",
                CreatedAt = clock.GetCurrentInstant(),
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            tenantId = tenant.Id;
        }

        // A fresh scope with the tenant set before any connection opens, so the EF filter and the RLS setting both apply.
        await tenantScopes.RunAsync(tenantId, async (provider, ct) =>
        {
            var db = provider.GetRequiredService<SalonDbContext>();
            var sortOrder = 0;
            foreach (var (name, description, minutes, price) in Services)
            {
                db.Services.Add(new Service
                {
                    Name = name,
                    Description = description,
                    Duration = Duration.FromMinutes(minutes),
                    Price = price,
                    Currency = "GBP",
                    SortOrder = ++sortOrder,
                });
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await SeedUsersAsync(provider.GetRequiredService<IUserService>(), ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Seeded demo tenant {Slug} with {Count} services", Slug, Services.Length);
        return true;
    }

    private async Task SeedUsersAsync(IUserService users, CancellationToken cancellationToken)
    {
        var password = demoOptions.Value.OwnerPassword;
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Demo:OwnerPassword is not configured; the demo salon has no logins. Set it in appsettings.Development.json (see docs/system-design.md for the demo accounts)");
            return;
        }

        await users.CreateAsync(OwnerEmail, "Studio 20 Owner", password, Role.Owner, cancellationToken).ConfigureAwait(false);
        await users.CreateAsync(ReceptionistEmail, "Studio 20 Front Desk", password, Role.Receptionist, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Seeded {Count} demo logins for tenant {Slug} (roles: Owner, Receptionist)", 2, Slug);
    }
}