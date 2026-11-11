using Salon.Core.Domain;
using Salon.Core.Events;
using Salon.Core.Tenancy;

namespace Salon.Tests.Architecture;

/// <summary>CLAUDE.md rule 4: every tenant-owned entity implements ITenantEntity. Only platform-level entities are exempt.</summary>
public class TenantEntityTests
{
    // OutboxMessage: the dispatcher reads every tenant's rows in one query (ADR 0007); consumers still run in the row's tenant scope.
    private static readonly Type[] PlatformLevelEntities = [typeof(Tenant), typeof(OutboxMessage)];

    [Fact]
    public void Every_entity_is_tenant_owned_unless_listed_as_platform_level()
    {
        var offenders = Assemblies.Core.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(BaseEntity).IsAssignableFrom(t))
            .Where(t => !PlatformLevelEntities.Contains(t))
            .Where(t => !typeof(ITenantEntity).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        offenders.ShouldBeEmpty();
    }
}