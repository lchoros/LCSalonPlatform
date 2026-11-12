using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NodaTime;

using Salon.Core.Catalog;
using Salon.Core.Identity;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Identity;
using Salon.Services.Tenancy;

namespace Salon.Services.Seeding;

/// <summary>
/// Creates the demo salon "studio-20" (Studio 20, Europe/London, en-GB, GBP) with six services in three categories and
/// realistic buffers and processing time (ADR 0013), three staff members with their weekly hours (ADR 0010) and, when
/// <c>Demo:OwnerPassword</c> is set, an Owner and a Receptionist login, so a fresh local database has something to show at
/// studio-20.localhost. Runs when <c>Database:SeedDemo</c> is true (Development only). An existing demo salon is left
/// alone — its services and logins are never re-seeded — with two exceptions: a salon seeded before M1 has no staff and
/// gets the same three people the first time the seeder sees it without any, and a salon without categories gets the
/// three categories once, with the six demo services (matched by name) filed under them and given their timing.
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

    /// <summary>In display order; the sort order of each category is its position here.</summary>
    private static readonly string[] Categories = ["Hair", "Barbering", "Nails"];

    private static readonly DemoService[] Services =
    [
        new("Cut & blow-dry", "Consultation, wash, cut and finish.", 45, 45m, "Hair", Buffer: 5),
        new("Colour (roots)", "Regrowth tint up to 2 cm, blow-dry included.", 90, 65m, "Hair", Buffer: 15, Processing: 30, ProcessingStartsAfter: 20),
        new("Balayage", "Hand-painted lightening, toner and blow-dry.", 150, 140m, "Hair", Buffer: 15, Processing: 45, ProcessingStartsAfter: 45),
        new("Beard trim", "Shape, trim and hot-towel finish.", 20, 15m, "Barbering", Buffer: 5),
        new("Manicure", "File, cuticle care and polish.", 45, 28m, "Nails", Buffer: 5),
        new("Pedicure", "Soak, file, cuticle care and polish.", 60, 35m, "Nails", Buffer: 5),
    ];

    /// <summary>A colourist, a barber with a lunch break (two ranges) and a nail technician on alternate days: the shapes the rota screens must show.</summary>
    private static readonly DemoStaff[] Staff =
    [
        new("Anna Kowalska", "#BE185D",
            [IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday, IsoDayOfWeek.Saturday],
            [new TimeRange(new LocalTime(10, 0), new LocalTime(18, 0))]),
        new("Tom Reed", "#1D4ED8",
            [IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday],
            [new TimeRange(new LocalTime(9, 0), new LocalTime(13, 0)), new TimeRange(new LocalTime(14, 0), new LocalTime(17, 0))]),
        new("Priya Shah", "#047857",
            [IsoDayOfWeek.Monday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Friday],
            [new TimeRange(new LocalTime(9, 0), new LocalTime(15, 0))]),
    ];

    /// <summary>Returns true when the tenant was created on this call, false when it already existed (even if staff or categories were added to it).</summary>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        Guid tenantId;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var existingId = await db.Tenants.Where(t => t.Slug == Slug).Select(t => (Guid?)t.Id).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (existingId is { } existing)
            {
                logger.LogDebug("Demo tenant {Slug} already exists; only missing logins, staff and categories are seeded", Slug);
                await SeedMissingUsersAsync(existing, cancellationToken).ConfigureAwait(false);
                await tenantScopes.RunAsync(existing, async (provider, ct) =>
                {
                    var tenantDb = provider.GetRequiredService<SalonDbContext>();
                    if (await SeedStaffAsync(tenantDb, ct).ConfigureAwait(false))
                    {
                        logger.LogInformation("Seeded {Count} demo staff into the existing tenant {Slug}", Staff.Length, Slug);
                    }

                    if (await SeedCategoriesAsync(tenantDb, ct).ConfigureAwait(false))
                    {
                        logger.LogInformation("Seeded {Count} demo categories into the existing tenant {Slug}", Categories.Length, Slug);
                    }
                }, cancellationToken).ConfigureAwait(false);
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
            foreach (var service in Services)
            {
                db.Services.Add(new Service
                {
                    Name = service.Name,
                    Description = service.Description,
                    Duration = Duration.FromMinutes(service.Minutes),
                    Price = service.Price,
                    Currency = "GBP",
                    SortOrder = ++sortOrder,
                });
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await SeedCategoriesAsync(db, ct).ConfigureAwait(false);
            await SeedStaffAsync(db, ct).ConfigureAwait(false);
            await SeedUsersAsync(provider.GetRequiredService<IUserService>(), ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Seeded demo tenant {Slug} with {ServiceCount} services and {StaffCount} staff", Slug, Services.Length, Staff.Length);
        return true;
    }

    /// <summary>
    /// Adds the three demo categories when the salon in scope has none at all, and files the demo services (matched by
    /// name, only those still uncategorised) under them with their buffer and processing time; returns false otherwise.
    /// </summary>
    private static async Task<bool> SeedCategoriesAsync(SalonDbContext db, CancellationToken cancellationToken)
    {
        if (await db.ServiceCategories.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var categories = Categories
            .Select((name, index) => new ServiceCategory { Name = name, SortOrder = index + 1 })
            .ToDictionary(c => c.Name, StringComparer.Ordinal);
        db.ServiceCategories.AddRange(categories.Values);

        var existing = await db.Services.Where(s => s.CategoryId == null).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var service in existing)
        {
            var demo = Services.FirstOrDefault(d => string.Equals(d.Name, service.Name, StringComparison.Ordinal));
            if (demo is null)
            {
                continue;
            }

            service.CategoryId = categories[demo.Category].Id;
            service.BufferAfterMinutes = demo.Buffer;
            service.ProcessingMinutes = demo.Processing;
            service.ProcessingStartsAfterMinutes = demo.ProcessingStartsAfter;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Adds the three demo staff with their rota when the salon in scope has no staff at all; returns false otherwise.</summary>
    private async Task<bool> SeedStaffAsync(SalonDbContext db, CancellationToken cancellationToken)
    {
        if (await db.StaffMembers.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var now = clock.GetCurrentInstant();
        var sortOrder = 0;
        foreach (var person in Staff)
        {
            var member = new StaffMember { DisplayName = person.Name, Colour = person.Colour, SortOrder = ++sortOrder, CreatedAt = now };
            db.StaffMembers.Add(member);

            var rangeOrder = 0;
            foreach (var day in person.Days)
            {
                foreach (var range in person.Ranges)
                {
                    db.WorkingHours.Add(new WorkingHours { StaffMemberId = member.Id, Day = day, Start = range.Start, End = range.End, SortOrder = ++rangeOrder });
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>A demo salon seeded before logins existed (or before the password was configured) gets its two logins on the next start.</summary>
    private Task SeedMissingUsersAsync(Guid tenantId, CancellationToken cancellationToken)
        => tenantScopes.RunAsync(tenantId, async (provider, ct) =>
        {
            var db = provider.GetRequiredService<SalonDbContext>();
            if (!await db.Users.AnyAsync(ct).ConfigureAwait(false))
            {
                await SeedUsersAsync(provider.GetRequiredService<IUserService>(), ct).ConfigureAwait(false);
            }
        }, cancellationToken);

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

    /// <param name="Buffer">Clean-up minutes after the service (ADR 0013).</param>
    /// <param name="Processing">Minutes the client waits while the stylist is free; 0 for none.</param>
    /// <param name="ProcessingStartsAfter">Where inside the service the processing window starts.</param>
    private sealed record DemoService(string Name, string Description, int Minutes, decimal Price, string Category, int Buffer = 0, int Processing = 0, int ProcessingStartsAfter = 0);

    private sealed record DemoStaff(string Name, string Colour, IReadOnlyList<IsoDayOfWeek> Days, IReadOnlyList<TimeRange> Ranges);
}