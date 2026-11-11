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
/// Creates the demo salon "studio-20" (Studio 20, Europe/London, en-GB, GBP) with six services, three staff members with
/// their weekly hours (ADR 0010) and, when <c>Demo:OwnerPassword</c> is set, an Owner and a Receptionist login, so a fresh
/// local database has something to show at studio-20.localhost. Runs when <c>Database:SeedDemo</c> is true (Development
/// only). An existing demo salon is left alone — its catalogue and logins are never touched — with one exception: a salon
/// seeded before M1 has no staff, and gets the same three people the first time the seeder sees it without any.
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

    /// <summary>Returns true when the tenant was created on this call, false when it already existed (even if staff were added to it).</summary>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        Guid tenantId;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var existingId = await db.Tenants.Where(t => t.Slug == Slug).Select(t => (Guid?)t.Id).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (existingId is { } existing)
            {
                logger.LogDebug("Demo tenant {Slug} already exists; only missing staff are seeded", Slug);
                await tenantScopes.RunAsync(existing, async (provider, ct) =>
                {
                    if (await SeedStaffAsync(provider.GetRequiredService<SalonDbContext>(), ct).ConfigureAwait(false))
                    {
                        logger.LogInformation("Seeded {Count} demo staff into the existing tenant {Slug}", Staff.Length, Slug);
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
            await SeedStaffAsync(db, ct).ConfigureAwait(false);
            await SeedUsersAsync(provider.GetRequiredService<IUserService>(), ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Seeded demo tenant {Slug} with {ServiceCount} services and {StaffCount} staff", Slug, Services.Length, Staff.Length);
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

    private sealed record DemoStaff(string Name, string Colour, IReadOnlyList<IsoDayOfWeek> Days, IReadOnlyList<TimeRange> Ranges);
}