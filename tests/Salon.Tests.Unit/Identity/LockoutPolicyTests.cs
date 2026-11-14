using NodaTime;
using Salon.Core.Identity;

namespace Salon.Tests.Unit.Identity;

/// <summary>Five failures lock for fifteen minutes; a success clears everything (ADR 0009). Pure rules, fixed clock.</summary>
public class LockoutPolicyTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 11, 10, 0);

    private static User NewUser() => new()
    {
        Email = "a@b.c",
        DisplayName = "A",
        PasswordHash = "hash",
        SecurityStamp = "stamp",
    };

    [Fact]
    public void Four_failures_count_but_do_not_lock()
    {
        var user = NewUser();

        for (var i = 0; i < 4; i++)
        {
            LockoutPolicy.RecordFailure(user, Now).ShouldBeFalse();
        }

        user.FailedLoginCount.ShouldBe(4);
        user.LockedOutUntil.ShouldBeNull();
        LockoutPolicy.IsLockedOut(user, Now).ShouldBeFalse();
    }

    [Fact]
    public void The_fifth_failure_locks_for_fifteen_minutes_and_resets_the_count()
    {
        var user = NewUser();
        for (var i = 0; i < 4; i++)
        {
            LockoutPolicy.RecordFailure(user, Now);
        }

        LockoutPolicy.RecordFailure(user, Now).ShouldBeTrue();

        user.FailedLoginCount.ShouldBe(0);
        user.LockedOutUntil.ShouldBe(Now + Duration.FromMinutes(15));
        LockoutPolicy.IsLockedOut(user, Now).ShouldBeTrue();
        LockoutPolicy.IsLockedOut(user, Now + Duration.FromMinutes(14) + Duration.FromSeconds(59)).ShouldBeTrue();
        LockoutPolicy.IsLockedOut(user, Now + Duration.FromMinutes(15)).ShouldBeFalse();
    }

    [Fact]
    public void After_the_lock_expires_failures_count_from_zero_again()
    {
        var user = NewUser();
        for (var i = 0; i < 5; i++)
        {
            LockoutPolicy.RecordFailure(user, Now);
        }

        var later = Now + Duration.FromMinutes(16);
        LockoutPolicy.RecordFailure(user, later).ShouldBeFalse();

        user.FailedLoginCount.ShouldBe(1);
        LockoutPolicy.IsLockedOut(user, later).ShouldBeFalse();
    }

    [Fact]
    public void A_success_clears_the_count_and_the_lock_and_records_the_time()
    {
        var user = NewUser();
        LockoutPolicy.RecordFailure(user, Now);
        LockoutPolicy.RecordFailure(user, Now);
        user.LockedOutUntil = Now - Duration.FromMinutes(1);

        LockoutPolicy.RecordSuccess(user, Now);

        user.FailedLoginCount.ShouldBe(0);
        user.LockedOutUntil.ShouldBeNull();
        user.LastLoginAt.ShouldBe(Now);
    }

    [Fact]
    public void The_constants_are_the_ADR_values()
    {
        LockoutPolicy.MaxFailedAttempts.ShouldBe(5);
        LockoutPolicy.LockoutDuration.ShouldBe(Duration.FromMinutes(15));
    }
}