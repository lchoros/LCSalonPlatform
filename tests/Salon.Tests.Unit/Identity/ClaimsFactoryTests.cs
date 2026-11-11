using Salon.Core.Identity;
using Salon.Services.Identity;

namespace Salon.Tests.Unit.Identity;

public class ClaimsFactoryTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();

    private static User Receptionist() => new()
    {
        TenantId = TenantId,
        Email = "desk@studio-20.local",
        DisplayName = "Front Desk",
        PasswordHash = "hash",
        Role = Role.Receptionist,
        SecurityStamp = "stamp-1",
    };

    [Fact]
    public void The_principal_carries_id_tenant_name_role_stamp_and_one_claim_per_permission()
    {
        var user = Receptionist();

        var principal = ClaimsFactory.Create(user, TenantId);

        principal.Identity.ShouldNotBeNull().IsAuthenticated.ShouldBeTrue();
        principal.Identity.AuthenticationType.ShouldBe("salon.auth");
        principal.Identity.Name.ShouldBe("Front Desk");
        principal.FindFirst(UserClaimTypes.Subject)!.Value.ShouldBe(user.Id.ToString());
        principal.FindFirst(UserClaimTypes.TenantId)!.Value.ShouldBe(TenantId.ToString());
        principal.FindFirst(UserClaimTypes.Role)!.Value.ShouldBe("Receptionist");
        principal.IsInRole("Receptionist").ShouldBeTrue();
        principal.FindFirst(UserClaimTypes.SecurityStamp)!.Value.ShouldBe("stamp-1");
        principal.FindAll(UserClaimTypes.Permission).Select(c => c.Value)
            .ShouldBe(RolePermissions.For(Role.Receptionist), ignoreOrder: true);
        principal.HasClaim(UserClaimTypes.Permission, Permissions.CatalogManage).ShouldBeFalse();
    }

    [Fact]
    public void An_owner_gets_every_permission_claim()
    {
        var owner = Receptionist();
        owner.Role = Role.Owner;

        var principal = ClaimsFactory.Create(owner, TenantId);

        principal.FindAll(UserClaimTypes.Permission).Select(c => c.Value).ShouldBe(Permissions.All, ignoreOrder: true);
    }

    [Fact]
    public void Nothing_secret_leaks_into_the_claims()
    {
        var principal = ClaimsFactory.Create(Receptionist(), TenantId);

        principal.Claims.ShouldNotContain(c => c.Value == "hash");
        principal.Claims.Select(c => c.Type).ShouldBe(
            [UserClaimTypes.Subject, UserClaimTypes.TenantId, UserClaimTypes.Name, UserClaimTypes.Role, UserClaimTypes.SecurityStamp, UserClaimTypes.Permission, UserClaimTypes.Permission, UserClaimTypes.Permission],
            ignoreOrder: true);
    }

    [Fact]
    public void A_user_of_another_tenant_is_refused()
    {
        Should.Throw<ArgumentException>(() => ClaimsFactory.Create(Receptionist(), Guid.CreateVersion7()));
    }
}