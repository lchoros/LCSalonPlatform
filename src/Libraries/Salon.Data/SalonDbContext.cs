using Microsoft.EntityFrameworkCore;

using Salon.Core.Tenancy;

namespace Salon.Data;

/// <summary>
/// Single DbContext for the platform. Tenant-owned entities get a global query filter on TenantId (added with the
/// tenancy slice); mappings live in <c>Configurations/</c>, one class per entity.
/// </summary>
public class SalonDbContext(DbContextOptions<SalonDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema("salon");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SalonDbContext).Assembly);
    }
}