using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Salon.Core.Identity;
using Salon.Services.Identity;
using Salon.Web.Framework.Identity;

namespace Salon.Tests.Unit.Identity;

/// <summary>PBKDF2 through PasswordHasher&lt;User&gt; (ADR 0009): salted, verifiable, and flagged for rehash when the parameters grew.</summary>
public class PasswordServiceTests
{
    private const string Password = "Correct-horse-9";

    private static readonly User User = new()
    {
        Email = "a@b.c",
        DisplayName = "A",
        PasswordHash = string.Empty,
        SecurityStamp = "s",
    };

    private readonly PasswordHasherPasswordService _service = new(Options.Create(new PasswordHasherOptions()));

    [Fact]
    public void A_hash_is_not_the_password_and_two_hashes_of_one_password_differ()
    {
        var first = _service.Hash(User, Password);
        var second = _service.Hash(User, Password);

        first.ShouldNotBe(Password);
        first.ShouldNotContain(Password);
        first.ShouldNotBe(second);
    }

    [Fact]
    public void The_right_password_verifies_and_a_wrong_one_does_not()
    {
        var hash = _service.Hash(User, Password);

        _service.Verify(User, hash, Password).ShouldBe(PasswordVerification.Success);
        _service.Verify(User, hash, "correct-horse-9").ShouldBe(PasswordVerification.Failed);
        _service.Verify(User, hash, string.Empty).ShouldBe(PasswordVerification.Failed);
        _service.Verify(User, string.Empty, Password).ShouldBe(PasswordVerification.Failed);
        _service.Verify(User, "not-a-hash", Password).ShouldBe(PasswordVerification.Failed);
    }

    [Fact]
    public void A_hash_made_with_fewer_iterations_verifies_but_asks_for_a_rehash()
    {
        var weaker = new PasswordHasherPasswordService(Options.Create(new PasswordHasherOptions { IterationCount = 1_000 }));
        var hash = weaker.Hash(User, Password);

        _service.Verify(User, hash, Password).ShouldBe(PasswordVerification.SuccessRehashNeeded);
    }

    [Fact]
    public void An_empty_password_cannot_be_hashed()
    {
        Should.Throw<ArgumentException>(() => _service.Hash(User, string.Empty));
    }
}