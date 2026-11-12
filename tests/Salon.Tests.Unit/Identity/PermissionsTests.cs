using Salon.Core.Identity;
using Salon.Web.Framework.Tenancy;

namespace Salon.Tests.Unit.Identity;

/// <summary>The permission catalogue and the role map of ADR 0009.</summary>
public class PermissionsTests
{
    [Fact]
    public void The_catalogue_holds_the_nine_names_once_each()
    {
        Permissions.All.Count.ShouldBe(9);
        Permissions.All.Distinct(StringComparer.Ordinal).Count().ShouldBe(9);
        Permissions.All.ShouldBe(
        [
            "Catalog.Manage", "Staff.Manage", "Bookings.Manage", "Clients.View", "Clients.Manage",
            "Payments.Record", "Settings.Manage", "Templates.Manage", "Users.Manage",
        ], ignoreOrder: true);
    }

    [Fact]
    public void Owner_has_every_permission()
    {
        RolePermissions.For(Role.Owner).ShouldBe(Permissions.All, ignoreOrder: true);
    }

    [Fact]
    public void Manager_has_everything_but_users_and_settings()
    {
        var manager = RolePermissions.For(Role.Manager);

        manager.ShouldNotContain(Permissions.UsersManage);
        manager.ShouldNotContain(Permissions.SettingsManage);
        manager.ShouldBe(Permissions.All.Except([Permissions.UsersManage, Permissions.SettingsManage]), ignoreOrder: true);
    }

    [Fact]
    public void Receptionist_books_sees_clients_and_records_payments()
    {
        RolePermissions.For(Role.Receptionist).ShouldBe([Permissions.BookingsManage, Permissions.ClientsView, Permissions.PaymentsRecord], ignoreOrder: true);
        RolePermissions.For(Role.Receptionist).ShouldNotContain(Permissions.ClientsManage);
    }

    [Fact]
    public void Staff_only_books()
    {
        RolePermissions.For(Role.Staff).ShouldBe([Permissions.BookingsManage]);
    }

    [Theory]
    [InlineData(Role.Owner)]
    [InlineData(Role.Manager)]
    [InlineData(Role.Receptionist)]
    [InlineData(Role.Staff)]
    public void Every_role_maps_to_known_permissions_only(Role role)
    {
        RolePermissions.For(role).ShouldAllBe(p => Permissions.All.Contains(p));
        RolePermissions.For(role).ShouldNotBeEmpty();
    }

    [Fact]
    public void An_unknown_role_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RolePermissions.For((Role)99));
    }

    [Fact]
    public void The_tenant_claim_type_is_what_the_tenant_middleware_reads_by_default()
    {
        new TenancyOptions().TenantClaimType.ShouldBe(UserClaimTypes.TenantId);
        UserClaimTypes.TenantId.ShouldBe("tenant_id");
    }
}