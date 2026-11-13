using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Salon.Core.Appointments;
using Salon.Core.Booking;
using Salon.Core.Catalog;
using Salon.Core.Clients;
using Salon.Core.Events;
using Salon.Core.Identity;
using Salon.Core.Locations;
using Salon.Core.Messaging;
using Salon.Core.Notifications;
using Salon.Core.Payments;
using Salon.Core.Settings;
using Salon.Core.Staff;
using Salon.Core.Tenancy;

namespace Salon.Data;

/// <summary>
/// Single DbContext for the platform. Every <see cref="ITenantEntity"/> gets the named query filter
/// <see cref="TenantFilterName"/> on TenantId, and SaveChanges refuses writes outside the current tenant.
/// Postgres row-level security backs both (see Tenancy/TenantConnectionInterceptor and Migrations/RowLevelSecurity).
/// </summary>
public class SalonDbContext(DbContextOptions<SalonDbContext> options, ITenantContext tenantContext) : DbContext(options)
{
    public const string TenantFilterName = "Tenant";

    private static readonly MethodInfo BuildTenantFilterMethod =
        typeof(SalonDbContext).GetMethod(nameof(BuildTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Service> Services => Set<Service>();

    /// <summary>Headings the salon groups its services under (ADR 0013).</summary>
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();

    /// <summary>Per-person duration, price or "does not offer" for a service (ADR 0018).</summary>
    public DbSet<StaffServiceOverride> StaffServiceOverrides => Set<StaffServiceOverride>();
    /// <summary>Client cards (ADR 0011). Health notes are cipher text here; decrypt only through the client service.</summary>
    public DbSet<Client> Clients => Set<Client>();

    public DbSet<Setting> Settings => Set<Setting>();

    public DbSet<User> Users => Set<User>();

    /// <summary>Bookable people of the salon (ADR 0010); their weekly rota is <see cref="WorkingHours"/>.</summary>
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();

    /// <summary>Weekly working-hour ranges per staff member, in the salon's local time (ADR 0010).</summary>
    public DbSet<WorkingHours> WorkingHours => Set<WorkingHours>();

    /// <summary>Planned absences per staff member: local dates and an optional daily time range (ADR 0015).</summary>
    public DbSet<StaffTimeOff> StaffTimeOff => Set<StaffTimeOff>();

    /// <summary>Booked visits (ADR 0012); Postgres refuses overlapping active appointments of one staff member.</summary>
    public DbSet<Appointment> Appointments => Set<Appointment>();

    /// <summary>One row per status transition of an appointment (ADR 0012).</summary>
    public DbSet<AppointmentStatusChange> AppointmentStatusChanges => Set<AppointmentStatusChange>();

    /// <summary>SMS codes sent to clients booking online, each carrying its pending booking (ADR 0014).</summary>
    public DbSet<PhoneVerification> PhoneVerifications => Set<PhoneVerification>();

    /// <summary>One row per reminder handled for an appointment; the unique row is what prevents a second SMS (ADR 0020).</summary>
    public DbSet<AppointmentReminder> AppointmentReminders => Set<AppointmentReminder>();

    /// <summary>What clients owe the salon and how it was settled; lines are only ever added (ADR 0022).</summary>
    public DbSet<ClientAccountEntry> ClientAccountEntries => Set<ClientAccountEntry>();

    /// <summary>One end-of-day count of the cash drawer per salon and day (ADR 0024).</summary>
    public DbSet<CashUp> CashUps => Set<CashUp>();

    /// <summary>Where the salon is; the first active row is the main location (ADR 0027).</summary>
    public DbSet<Location> Locations => Set<Location>();

    /// <summary>Per-tenant overrides of the embedded message template defaults (ADR 0008).</summary>
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();

    /// <summary>Platform-level (no tenant filter, no RLS): the dispatcher reads every tenant's pending events (ADR 0007).</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Read inside the query filter; EF turns it into a query parameter evaluated per execution.</summary>
    private Guid? CurrentTenantId => tenantContext.TenantId;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantOwnership();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceTenantOwnership();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema("salon");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SalonDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var filter = (LambdaExpression)BuildTenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, null)!;
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(TenantFilterName, filter);
            modelBuilder.Entity(entityType.ClrType).HasIndex(nameof(ITenantEntity.TenantId));
        }
    }

    private LambdaExpression BuildTenantFilter<TEntity>()
        where TEntity : class, ITenantEntity
        => (Expression<Func<TEntity, bool>>)(e => e.TenantId == CurrentTenantId);

    private void EnforceTenantOwnership()
    {
        var current = tenantContext.TenantId;

        foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (current is null)
            {
                throw new TenantViolationException(
                    $"Cannot save {entry.Metadata.ClrType.Name} without a current tenant.");
            }

            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
            {
                entry.Entity.TenantId = current.Value;
            }

            if (entry.State == EntityState.Modified && entry.Property(e => e.TenantId).IsModified)
            {
                throw new TenantViolationException(
                    $"{entry.Metadata.ClrType.Name} cannot move between tenants.");
            }

            if (entry.Entity.TenantId != current.Value)
            {
                throw new TenantViolationException(
                    $"{entry.Metadata.ClrType.Name} belongs to another tenant than the current one.");
            }
        }
    }
}