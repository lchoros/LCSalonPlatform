using NodaTime;
using Salon.Core.Catalog;
using Salon.Core.Catalog.Events;
using Salon.Core.Events;
using Salon.Core.Tenancy;

namespace Salon.Tests.Unit.Catalog;

/// <summary>Defaults of the new catalogue shapes (ADR 0013): a category is active and tenant-owned; a service starts without category, buffer or processing time.</summary>
public class ServiceCategoryTests
{
    [Fact]
    public void A_new_category_is_active_tenant_owned_and_unsorted()
    {
        var category = new ServiceCategory { Name = "Hair" };

        category.Id.Version.ShouldBe(7);
        category.TenantId.ShouldBe(Guid.Empty, "the DbContext stamps the current tenant on save");
        category.IsActive.ShouldBeTrue();
        category.SortOrder.ShouldBe(0);
        typeof(ITenantEntity).IsAssignableFrom(typeof(ServiceCategory)).ShouldBeTrue();
    }

    [Fact]
    public void A_new_service_has_no_category_buffer_or_processing_time()
    {
        var service = new Service { Name = "Cut", Currency = "GBP", Duration = Duration.FromMinutes(45) };

        service.CategoryId.ShouldBeNull();
        service.BufferAfterMinutes.ShouldBe(0);
        service.ProcessingMinutes.ShouldBe(0);
        service.ProcessingStartsAfterMinutes.ShouldBe(0);
        service.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void The_catalogue_events_carry_ids_only()
    {
        var serviceId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();

        IEvent updated = new ServiceUpdated(serviceId, tenantId);
        IEvent deactivated = new ServiceDeactivated(serviceId, tenantId);

        updated.ShouldBe(new ServiceUpdated(serviceId, tenantId));
        deactivated.ShouldBe(new ServiceDeactivated(serviceId, tenantId));
        typeof(ServiceUpdated).GetProperties().Select(p => p.Name).Order().ShouldBe(["ServiceId", "TenantId"]);
        typeof(ServiceDeactivated).GetProperties().Select(p => p.Name).Order().ShouldBe(["ServiceId", "TenantId"]);
    }
}