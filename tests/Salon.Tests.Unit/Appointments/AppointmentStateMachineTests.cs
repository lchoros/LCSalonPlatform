using NodaTime;

using Salon.Core.Appointments;

using static Salon.Core.Appointments.AppointmentStatus;

namespace Salon.Tests.Unit.Appointments;

/// <summary>ADR 0012: the full transition table, and what <see cref="AppointmentStateMachine.Transition"/> changes and returns.</summary>
public class AppointmentStateMachineTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 11, 10, 30);
    private static readonly Guid UserId = Guid.Parse("0190a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b");

    private static readonly HashSet<(AppointmentStatus From, AppointmentStatus To)> AllowedPairs =
    [
        (Booked, Confirmed), (Booked, Arrived), (Booked, NoShow), (Booked, CancelledByClient), (Booked, CancelledBySalon),
        (Confirmed, Arrived), (Confirmed, NoShow), (Confirmed, CancelledByClient), (Confirmed, CancelledBySalon),
        (Arrived, Completed), (Arrived, CancelledByClient), (Arrived, CancelledBySalon),
    ];

    public static TheoryData<AppointmentStatus, AppointmentStatus> EveryPair()
    {
        var data = new TheoryData<AppointmentStatus, AppointmentStatus>();
        foreach (var from in Enum.GetValues<AppointmentStatus>())
        {
            foreach (var to in Enum.GetValues<AppointmentStatus>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Can_transition_matches_the_table_for_every_pair(AppointmentStatus from, AppointmentStatus to)
    {
        AppointmentStateMachine.CanTransition(from, to).ShouldBe(AllowedPairs.Contains((from, to)), $"{from} → {to}");
    }

    [Fact]
    public void The_table_has_exactly_the_allowed_moves_of_the_adr()
    {
        var allowed = Enum.GetValues<AppointmentStatus>()
            .SelectMany(from => Enum.GetValues<AppointmentStatus>().Select(to => (from, to)))
            .Where(pair => AppointmentStateMachine.CanTransition(pair.from, pair.to))
            .ToHashSet();

        allowed.ShouldBe(AllowedPairs);
    }

    [Theory]
    [InlineData(Completed)]
    [InlineData(NoShow)]
    [InlineData(CancelledByClient)]
    [InlineData(CancelledBySalon)]
    public void Finished_and_cancelled_states_are_terminal(AppointmentStatus status)
    {
        AppointmentStateMachine.IsTerminal(status).ShouldBeTrue();
        AppointmentStateMachine.IsActive(status).ShouldBeFalse();
    }

    [Theory]
    [InlineData(Booked)]
    [InlineData(Confirmed)]
    [InlineData(Arrived)]
    public void Booked_confirmed_and_arrived_are_active_and_not_terminal(AppointmentStatus status)
    {
        AppointmentStateMachine.IsActive(status).ShouldBeTrue();
        AppointmentStateMachine.IsTerminal(status).ShouldBeFalse();
        AppointmentStateMachine.Active.ShouldContain(status);
    }

    [Fact]
    public void Transition_changes_the_status_and_returns_the_history_row()
    {
        var appointment = NewAppointment(Booked);

        var change = AppointmentStateMachine.Transition(appointment, Confirmed, Now, UserId, null);

        appointment.Status.ShouldBe(Confirmed);
        appointment.CancelledAt.ShouldBeNull();
        appointment.CancellationReason.ShouldBeNull();
        change.TenantId.ShouldBe(appointment.TenantId);
        change.AppointmentId.ShouldBe(appointment.Id);
        change.From.ShouldBe(Booked);
        change.To.ShouldBe(Confirmed);
        change.At.ShouldBe(Now);
        change.ByUserId.ShouldBe(UserId);
        change.Reason.ShouldBeNull();
    }

    [Theory]
    [InlineData(CancelledByClient)]
    [InlineData(CancelledBySalon)]
    public void A_cancellation_stamps_when_and_why(AppointmentStatus to)
    {
        var appointment = NewAppointment(Confirmed);

        var change = AppointmentStateMachine.Transition(appointment, to, Now, null, "Client is ill");

        appointment.Status.ShouldBe(to);
        appointment.CancelledAt.ShouldBe(Now);
        appointment.CancellationReason.ShouldBe("Client is ill");
        change.Reason.ShouldBe("Client is ill");
        change.ByUserId.ShouldBeNull();
        AppointmentStateMachine.IsCancelled(to).ShouldBeTrue();
    }

    [Fact]
    public void A_forbidden_transition_throws_and_changes_nothing()
    {
        var appointment = NewAppointment(Booked);

        var ex = Should.Throw<InvalidAppointmentTransitionException>(
            () => AppointmentStateMachine.Transition(appointment, Completed, Now, UserId, "too soon"));

        ex.From.ShouldBe(Booked);
        ex.To.ShouldBe(Completed);
        ex.Message.ShouldContain("Booked");
        ex.Message.ShouldContain("Completed");
        appointment.Status.ShouldBe(Booked);
        appointment.CancelledAt.ShouldBeNull();
        appointment.CancellationReason.ShouldBeNull();
    }

    [Fact]
    public void A_terminal_appointment_cannot_move_anywhere()
    {
        foreach (var to in Enum.GetValues<AppointmentStatus>())
        {
            var appointment = NewAppointment(Completed);
            Should.Throw<InvalidAppointmentTransitionException>(() => AppointmentStateMachine.Transition(appointment, to, Now, null, null));
            appointment.Status.ShouldBe(Completed);
        }
    }

    private static Appointment NewAppointment(AppointmentStatus status) => new()
    {
        TenantId = Guid.NewGuid(),
        ClientId = Guid.NewGuid(),
        StaffMemberId = Guid.NewGuid(),
        ServiceId = Guid.NewGuid(),
        Start = Now.Plus(Duration.FromHours(2)),
        End = Now.Plus(Duration.FromHours(2) + Duration.FromMinutes(30)),
        Status = status,
        Source = AppointmentSource.Online,
        Currency = "GBP",
        PriceAmount = 20m,
        DurationMinutes = 30,
        CreatedAt = Now,
    };
}