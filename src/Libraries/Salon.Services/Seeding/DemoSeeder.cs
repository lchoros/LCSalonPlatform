using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Core.Identity;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Availability;
using Salon.Services.Clients;
using Salon.Services.Identity;
using Salon.Services.Tenancy;

namespace Salon.Services.Seeding;

/// <summary>
/// Creates the demo salon "studio-20" (Studio 20, Europe/London, en-GB, GBP) with six services in three categories and
/// realistic buffers and processing time (ADR 0013), three staff members with their weekly hours (ADR 0010), twelve clients
/// (ADR 0011, health notes encrypted through the client service), a handful of appointments over the coming working days
/// (ADR 0012, booked through the booking service so the engine and the state machine apply) and, when
/// <c>Demo:OwnerPassword</c> is set, an Owner and a Receptionist login, so a fresh local database has something to show at
/// studio-20.localhost. Runs when <c>Database:SeedDemo</c> is true (Development only). An existing demo salon is left
/// alone — its services and logins are never re-seeded — except that a salon seeded by an earlier milestone gets the pieces
/// it lacks, each only when it has none of that kind: staff, categories (the six demo services, matched by name, are filed
/// under them and given their timing), clients, appointments.
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

    /// <summary>The checkbox text the demo clients "ticked"; the same shape of wording the create form records (ADR 0011).</summary>
    public const string MarketingConsentWording = "I agree to Studio 20 contacting me about news and offers by SMS and email. I can withdraw this consent at any time.";

    /// <summary>How far ahead the appointment seed looks for days with free times (a rota with weekend gaps needs a few).</summary>
    private const int AppointmentLookaheadDays = 14;

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

    /// <summary>Twelve regulars: UK numbers in E.164, a few with health notes and patch-test dates, two Polish names, eight with marketing consent.</summary>
    private static readonly DemoClient[] Clients =
    [
        new("Sophie", "Smith", "+447700900011", "sophie.smith@example.com", "Prefers morning slots.", null, null, true),
        new("James", "Brown", "+447700900012", "james.brown@example.com", null, "PPD sensitivity — patch test required before any colour.", new LocalDate(2026, 5, 15), false),
        new("Lily", "Jones", "+447700900013", "lily.jones@example.com", "Loves the coffee machine.", null, null, true),
        new("Oliver", "Williams", "+447700900014", "oliver.williams@example.com", "Preferred stylist: Sarah.", "Irritant contact dermatitis.", null, true),
        new("Ava", "Taylor", "+447700900015", "ava.taylor@example.com", null, null, null, true),
        new("Ethan", "Wilson", "+447700900016", "ethan.wilson@example.com", "Parking can be tricky.", "Allergic to certain hair products.", new LocalDate(2026, 7, 20), false),
        new("Emma", "Moore", "+447700900017", "emma.moore@example.com", null, null, null, true),
        new("Noah", "Jackson", "+447700900018", "noah.jackson@example.com", "Quick appointments preferred.", null, null, true),
        new("Amelia", "White", "+447700900019", "amelia.white@example.com", null, "Sulfate sensitivity — patch test required.", new LocalDate(2026, 3, 10), true),
        new("Mia", "Green", "+447700900020", "mia.green@example.com", null, null, null, false),
        new("Mateusz", "Kowalski", "+447700900021", "mateusz.kowalski@example.com", "Lives in Salford.", null, null, true),
        new("Katarzyna", "Nowak", "+447700900022", "katarzyna.nowak@example.com", "Speaks Polish.", "Alopecia areata — patch test required before any treatment.", new LocalDate(2026, 9, 5), false),
    ];

    /// <summary>
    /// Upcoming visits: which of the next days with free times (0 = the first), which client (by name order), which of the
    /// first three services, and the status to leave them in. Each takes the first free slot of a person without a visit
    /// that day yet, so the day list shows every column in use.
    /// </summary>
    private static readonly (int Day, int Client, int Service, AppointmentStatus Status)[] UpcomingPlan =
    [
        (0, 1, 0, AppointmentStatus.Confirmed),
        (0, 2, 1, AppointmentStatus.Booked),
        (1, 3, 0, AppointmentStatus.Booked),
        (1, 4, 2, AppointmentStatus.CancelledByClient),
        (2, 5, 0, AppointmentStatus.Confirmed),
    ];

    /// <summary>Returns true when the tenant was created on this call, false when it already existed (even if staff, categories, clients or appointments were added to it).</summary>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        Guid tenantId;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var existingId = await db.Tenants.Where(t => t.Slug == Slug).Select(t => (Guid?)t.Id).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (existingId is { } existing)
            {
                logger.LogDebug("Demo tenant {Slug} already exists; only missing logins, staff, categories, clients and appointments are seeded", Slug);
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

                    if (await SeedClientsAsync(provider, ct).ConfigureAwait(false))
                    {
                        logger.LogInformation("Seeded {Count} demo clients into the existing tenant {Slug}", Clients.Length, Slug);
                    }

                    if (await SeedAppointmentsAsync(provider, ct).ConfigureAwait(false) is { } count)
                    {
                        logger.LogInformation("Seeded {Count} demo appointments into the existing tenant {Slug}", count, Slug);
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
        var appointments = 0;
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
            await SeedClientsAsync(provider, ct).ConfigureAwait(false);
            appointments = await SeedAppointmentsAsync(provider, ct).ConfigureAwait(false) ?? 0;
        }, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Seeded demo tenant {Slug} with {ServiceCount} services, {StaffCount} staff, {ClientCount} clients and {AppointmentCount} appointments",
            Slug, Services.Length, Staff.Length, Clients.Length, appointments);
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

    /// <summary>
    /// Adds the twelve demo clients through the client service (phones normalised, health notes encrypted, consent stamped
    /// with its wording, ADR 0011) when the salon in scope has no clients at all; returns false otherwise.
    /// </summary>
    private static async Task<bool> SeedClientsAsync(IServiceProvider provider, CancellationToken cancellationToken)
    {
        var db = provider.GetRequiredService<SalonDbContext>();
        if (await db.Clients.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var clients = provider.GetRequiredService<IClientService>();
        foreach (var client in Clients)
        {
            await clients.CreateAsync(
                new CreateClientRequest(
                    client.FirstName,
                    client.LastName,
                    client.Phone,
                    client.Email,
                    client.Notes,
                    client.HealthNotes,
                    client.LastPatchTestOn,
                    client.MarketingConsent,
                    client.MarketingConsent ? MarketingConsentWording : null),
                cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// When the salon in scope has no appointments at all: one visit completed yesterday (history rows written through the
    /// state machine; the past has no slots, so it cannot go through the engine) and up to five upcoming visits over today
    /// and the next two days with free times, each booked through the booking service and then confirmed or cancelled per
    /// <see cref="UpcomingPlan"/>. Anything the engine refuses is skipped. Returns the number added, or null when nothing
    /// was done (appointments exist already, or the salon lacks clients, services or bookable staff).
    /// </summary>
    private async Task<int?> SeedAppointmentsAsync(IServiceProvider provider, CancellationToken cancellationToken)
    {
        var db = provider.GetRequiredService<SalonDbContext>();
        if (await db.Appointments.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var clientIds = await db.Clients.AsNoTracking().OrderBy(c => c.LastName).ThenBy(c => c.FirstName).Select(c => c.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var services = await db.Services.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.SortOrder).Take(3).ToListAsync(cancellationToken).ConfigureAwait(false);
        var staffIds = await db.StaffMembers.AsNoTracking().Where(s => s.IsActive && s.IsBookable).OrderBy(s => s.SortOrder).Select(s => s.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (clientIds.Count == 0 || services.Count == 0 || staffIds.Count == 0)
        {
            return null;
        }

        var tenantId = provider.GetRequiredService<ITenantContext>().TenantId ?? throw new TenantViolationException("The demo seed runs inside a tenant scope.");
        var tenant = await provider.GetRequiredService<ITenantLookup>().FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb[tenant.TimeZoneId];
        var today = clock.GetCurrentInstant().InZone(zone).Date;

        AddCompletedYesterday(db, tenantId, clientIds[0], staffIds[0], services[0], zone.AtLeniently(today.PlusDays(-1).At(new LocalTime(10, 0))).ToInstant());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var added = 1;

        var availability = provider.GetRequiredService<IAvailabilityService>();
        var booking = provider.GetRequiredService<IBookingService>();
        var days = new List<LocalDate>();
        for (var day = today; day <= today.PlusDays(AppointmentLookaheadDays) && days.Count < 3; day = day.PlusDays(1))
        {
            if ((await availability.GetSlotsAsync(services[0].Id, null, day, day, cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                days.Add(day);
            }
        }

        var used = new HashSet<(LocalDate Day, Guid StaffMemberId)>();
        foreach (var (dayIndex, clientIndex, serviceIndex, status) in UpcomingPlan)
        {
            if (dayIndex >= days.Count)
            {
                continue;
            }

            var day = days[dayIndex];
            var service = services[serviceIndex % services.Count];
            var slots = await availability.GetSlotsAsync(service.Id, null, day, day, cancellationToken).ConfigureAwait(false);
            var slot = slots.FirstOrDefault(s => !used.Contains((day, s.StaffMemberId))) ?? (slots.Count > 0 ? slots[0] : null);
            if (slot is null)
            {
                continue;
            }

            try
            {
                var appointment = await booking.BookAsync(
                    new BookAppointmentRequest(clientIds[clientIndex % clientIds.Count], slot.StaffMemberId, service.Id, slot.Start, AppointmentSource.Admin),
                    cancellationToken).ConfigureAwait(false);
                switch (status)
                {
                    case AppointmentStatus.Confirmed:
                        await booking.ConfirmAsync(appointment.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
                        break;
                    case AppointmentStatus.CancelledByClient:
                        await booking.CancelAsync(appointment.Id, byClient: true, "Cannot make it this week.", cancellationToken: cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        break;
                }

                used.Add((day, slot.StaffMemberId));
                added++;
            }
            catch (SlotNoLongerAvailableException)
            {
                // Someone (another seed, a test) took it between the slot list and the insert; the demo does not need that one.
            }
        }

        return added;
    }

    /// <summary>A visit that ran its whole course yesterday: Booked two days before, Confirmed the day before, Arrived and Completed on the day.</summary>
    private static void AddCompletedYesterday(SalonDbContext db, Guid tenantId, Guid clientId, Guid staffMemberId, Service service, Instant start)
    {
        var createdAt = start.Minus(Duration.FromDays(2));
        var appointment = new Appointment
        {
            TenantId = tenantId,
            ClientId = clientId,
            StaffMemberId = staffMemberId,
            ServiceId = service.Id,
            Start = start,
            End = start.Plus(service.Duration),
            Status = AppointmentStatus.Booked,
            Source = AppointmentSource.Admin,
            PriceAmount = service.Price,
            Currency = service.Currency,
            DurationMinutes = (int)service.Duration.TotalMinutes,
            CreatedAt = createdAt,
        };
        db.Appointments.Add(appointment);
        db.AppointmentStatusChanges.Add(new AppointmentStatusChange { TenantId = tenantId, AppointmentId = appointment.Id, From = null, To = AppointmentStatus.Booked, At = createdAt });
        db.AppointmentStatusChanges.Add(AppointmentStateMachine.Transition(appointment, AppointmentStatus.Confirmed, start.Minus(Duration.FromDays(1)), null, null));
        db.AppointmentStatusChanges.Add(AppointmentStateMachine.Transition(appointment, AppointmentStatus.Arrived, start.Minus(Duration.FromMinutes(5)), null, null));
        db.AppointmentStatusChanges.Add(AppointmentStateMachine.Transition(appointment, AppointmentStatus.Completed, appointment.End, null, null));
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

    /// <param name="HealthNotes">Plain text here, in source; stored encrypted by the client service.</param>
    private sealed record DemoClient(
        string FirstName,
        string LastName,
        string Phone,
        string Email,
        string? Notes,
        string? HealthNotes,
        LocalDate? LastPatchTestOn,
        bool MarketingConsent);
}