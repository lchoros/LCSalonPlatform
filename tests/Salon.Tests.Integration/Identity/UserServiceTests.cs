using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Services.Identity;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Identity;

/// <summary>Users are tenant data (ADR 0009): unique per salon, invisible to the next salon, and every security change rotates the stamp.</summary>
[Collection(SharedDatabase.Name)]
public class UserServiceTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private Tenant _salonA = null!;
    private Tenant _salonB = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _salonA = await factory.CreateTenantAsync("users-a");
        _salonB = await factory.CreateTenantAsync("users-b");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_normalizes_the_email_hashes_the_password_and_stamps_the_user()
    {
        await using var scope = factory.CreateTenantScope(_salonA.Id);
        var users = Users(scope);

        var user = await users.CreateAsync("  Olivia@Studio-20.LOCAL ", "  Olivia  ", IdentityTestClient.Password, Role.Manager);

        user.TenantId.ShouldBe(_salonA.Id);
        user.Email.ShouldBe("olivia@studio-20.local");
        user.DisplayName.ShouldBe("Olivia");
        user.Role.ShouldBe(Role.Manager);
        user.IsActive.ShouldBeTrue();
        user.FailedLoginCount.ShouldBe(0);
        user.LockedOutUntil.ShouldBeNull();
        user.LastLoginAt.ShouldBeNull();
        user.SecurityStamp.ShouldNotBeNullOrWhiteSpace();
        user.PasswordHash.ShouldNotBeNullOrWhiteSpace();
        user.PasswordHash.ShouldNotContain(IdentityTestClient.Password);
        scope.ServiceProvider.GetRequiredService<IPasswordService>().Verify(user, user.PasswordHash, IdentityTestClient.Password).Succeeded.ShouldBeTrue();
        (await users.FindByEmailAsync("OLIVIA@studio-20.local")).ShouldNotBeNull().Id.ShouldBe(user.Id);
    }

    [Fact]
    public async Task The_same_email_is_refused_within_a_salon_but_fine_in_another_one()
    {
        await factory.CreateUserAsync(_salonA.Id, "shared@example.com", Role.Owner);

        await using (var scope = factory.CreateTenantScope(_salonA.Id))
        {
            await Should.ThrowAsync<InvalidOperationException>(() => Users(scope).CreateAsync("Shared@Example.com", "Twin", IdentityTestClient.Password, Role.Staff));
        }

        var inB = await factory.CreateUserAsync(_salonB.Id, "shared@example.com", Role.Staff);
        inB.TenantId.ShouldBe(_salonB.Id);

        await using (var scope = factory.CreateTenantScope(_salonB.Id))
        {
            (await Users(scope).FindByEmailAsync("shared@example.com")).ShouldNotBeNull().Id.ShouldBe(inB.Id);
        }

        await using (var scope = factory.CreateTenantScope(null))
        {
            (await Users(scope).FindByEmailAsync("shared@example.com")).ShouldBeNull("no tenant in scope must see nobody");
        }
    }

    [Theory]
    [InlineData("not-an-email", "Name", "Correct-horse-9")]
    [InlineData("a@b.c", "", "Correct-horse-9")]
    [InlineData("a@b.c", "Name", "short")]
    [InlineData("", "Name", "Correct-horse-9")]
    public async Task Invalid_input_is_refused_before_anything_is_written(string email, string displayName, string password)
    {
        await using var scope = factory.CreateTenantScope(_salonA.Id);

        await Should.ThrowAsync<ArgumentException>(() => Users(scope).CreateAsync(email, displayName, password, Role.Owner));

        (await Users(scope).FindByEmailAsync(email)).ShouldBeNull();
    }

    [Fact]
    public async Task Password_role_and_deactivation_changes_each_rotate_the_security_stamp()
    {
        var user = await factory.CreateUserAsync(_salonA.Id, "rotate@example.com", Role.Owner);
        var stamps = new List<string> { user.SecurityStamp };

        await using var scope = factory.CreateTenantScope(_salonA.Id);
        var users = Users(scope);

        await users.SetPasswordAsync(user.Id, "Another-pass-7");
        stamps.Add((await users.FindByIdAsync(user.Id))!.SecurityStamp);

        await users.SetRoleAsync(user.Id, Role.Receptionist);
        var afterRole = (await users.FindByIdAsync(user.Id))!;
        afterRole.Role.ShouldBe(Role.Receptionist);
        stamps.Add(afterRole.SecurityStamp);

        await users.DeactivateAsync(user.Id);
        var afterDeactivate = (await users.FindByIdAsync(user.Id))!;
        afterDeactivate.IsActive.ShouldBeFalse();
        stamps.Add(afterDeactivate.SecurityStamp);

        stamps.Distinct(StringComparer.Ordinal).Count().ShouldBe(4);
        var passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        passwords.Verify(afterDeactivate, afterDeactivate.PasswordHash, "Another-pass-7").Succeeded.ShouldBeTrue();
        passwords.Verify(afterDeactivate, afterDeactivate.PasswordHash, IdentityTestClient.Password).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Changing_a_user_of_another_salon_is_impossible()
    {
        var user = await factory.CreateUserAsync(_salonA.Id, "a-only@example.com", Role.Owner);

        await using var scope = factory.CreateTenantScope(_salonB.Id);

        await Should.ThrowAsync<InvalidOperationException>(() => Users(scope).SetRoleAsync(user.Id, Role.Staff));
        (await Users(scope).FindByIdAsync(user.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Sign_in_treats_an_inactive_user_like_a_wrong_password_and_still_counts_the_attempt()
    {
        var user = await factory.CreateUserAsync(_salonA.Id, "gone@example.com", Role.Owner);
        await using var scope = factory.CreateTenantScope(_salonA.Id);
        await Users(scope).DeactivateAsync(user.Id);

        var result = await scope.ServiceProvider.GetRequiredService<ISignInService>().AttemptAsync("gone@example.com", IdentityTestClient.Password);

        result.ShouldBe(SignInResult.Failed);
        (await Users(scope).FindByIdAsync(user.Id))!.FailedLoginCount.ShouldBe(1);
    }

    [Fact]
    public async Task Sign_in_for_an_unknown_email_fails_without_an_exception()
    {
        await using var scope = factory.CreateTenantScope(_salonA.Id);

        var result = await scope.ServiceProvider.GetRequiredService<ISignInService>().AttemptAsync("nobody@example.com", IdentityTestClient.Password);

        result.ShouldBe(SignInResult.Failed);
    }

    private static IUserService Users(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IUserService>();
}