using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Cqrs;

namespace Salon.Web.Features.Appointments.DayList;

/// <summary>
/// Moves one appointment of the current salon through the state machine (ADR 0012) from a button on the day list. The
/// handler answers rather than throws when the move is not allowed, so the controller can show the day again with a
/// message; an id the salon does not own surfaces as <see cref="Salon.Services.Appointments.AppointmentNotFoundException"/>.
/// </summary>
/// <param name="ByUserId">The signed-in user (the <c>sub</c> claim), recorded on the history row.</param>
public sealed record ChangeStatusCommand(Guid AppointmentId, AppointmentTransition Transition, Guid? ByUserId) : ICommand<ChangeStatusResult>;

/// <param name="Day">The appointment's local day, so the controller can return to the right list.</param>
/// <param name="Succeeded">False when the state machine refused the move; nothing changed then.</param>
/// <param name="From">The status before (and, when refused, still).</param>
/// <param name="To">The status asked for.</param>
/// <param name="FeeProposed">True when the salon's cancellation policy allows a fee for the visit as it is now (ADR 0022).</param>
public sealed record ChangeStatusResult(LocalDate Day, bool Succeeded, AppointmentStatus From, AppointmentStatus To, bool FeeProposed = false);

/// <summary>The buttons on an appointment card; each maps to one target status and one URL segment.</summary>
public enum AppointmentTransition
{
    Confirm,
    Arrived,
    Complete,
    NoShow,
    Cancel,
}

public static class AppointmentTransitions
{
    /// <summary>In button order.</summary>
    public static IReadOnlyList<AppointmentTransition> All { get; } =
        [AppointmentTransition.Confirm, AppointmentTransition.Arrived, AppointmentTransition.Complete, AppointmentTransition.NoShow, AppointmentTransition.Cancel];

    private static readonly Dictionary<string, AppointmentTransition> BySegment = new(StringComparer.Ordinal)
    {
        ["confirm"] = AppointmentTransition.Confirm,
        ["arrived"] = AppointmentTransition.Arrived,
        ["complete"] = AppointmentTransition.Complete,
        ["no-show"] = AppointmentTransition.NoShow,
        ["cancel"] = AppointmentTransition.Cancel,
    };

    /// <summary>The status the transition lands on; a cancellation from the admin is always by the salon.</summary>
    public static AppointmentStatus Target(AppointmentTransition transition) => transition switch
    {
        AppointmentTransition.Confirm => AppointmentStatus.Confirmed,
        AppointmentTransition.Arrived => AppointmentStatus.Arrived,
        AppointmentTransition.Complete => AppointmentStatus.Completed,
        AppointmentTransition.NoShow => AppointmentStatus.NoShow,
        AppointmentTransition.Cancel => AppointmentStatus.CancelledBySalon,
        _ => throw new ArgumentOutOfRangeException(nameof(transition), transition, "Unknown transition."),
    };

    /// <summary>The URL segment of <c>admin/appointments/{id}/{segment}</c>.</summary>
    public static string Segment(AppointmentTransition transition) => BySegment.First(p => p.Value == transition).Key;

    public static bool TryParse(string? segment, out AppointmentTransition transition)
        => BySegment.TryGetValue(segment ?? string.Empty, out transition);
}