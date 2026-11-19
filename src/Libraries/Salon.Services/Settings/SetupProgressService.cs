using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Settings;
using Salon.Data;
using Salon.Services.Locations;

namespace Salon.Services.Settings;

/// <summary>A step of setting a salon up, as the salon's data says it stands (ADR 0068, ADR 0072).</summary>
/// <param name="Done">What the salon's data says, not a box somebody ticked.</param>
/// <param name="Optional">A step a salon may leave: it is not counted toward "all done".</param>
/// <param name="Counts">The numbers the step's sentence names, in order.</param>
public sealed record SetupStepState(SetupStepKey Key, bool Done, bool Optional, IReadOnlyList<int> Counts);

/// <summary>How far a salon's set-up has come, read fresh from its data.</summary>
public sealed record SetupProgress(IReadOnlyList<SetupStepState> Steps)
{
    public int Required => Steps.Count(s => !s.Optional);

    public int RequiredDone => Steps.Count(s => !s.Optional && s.Done);

    public bool AllDone => RequiredDone == Required;
}

/// <summary>
/// What is set up of the current salon and what is left (ADR 0068). In <c>Salon.Services</c> because two modules read
/// it: the getting started page and the diary's note while set-up is unfinished (ADR 0072).
/// </summary>
public interface ISetupProgressService
{
    /// <summary>The steps in the order the getting started page lists them. Reads through the tenant filter only.</summary>
    Task<SetupProgress> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads a handful of counts through the tenant filter. Each step is "done" when the salon's data says so, so the
/// answer is never out of date and needs no box of its own; nothing is cached, the counts are small and indexed.
/// </summary>
public sealed class SetupProgressService(
    SalonDbContext db,
    ILocationService locations,
    ISettingService settings,
    IClock clock) : ISetupProgressService
{
    public async Task<SetupProgress> GetAsync(CancellationToken cancellationToken = default)
    {
        var location = await locations.GetMainAsync(cancellationToken).ConfigureAwait(false);
        var bookable = await db.StaffMembers.AsNoTracking()
            .Where(s => s.IsActive && s.IsBookable)
            .Select(s => new { s.Id, HasHours = db.WorkingHours.Any(w => w.StaffMemberId == s.Id) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var services = await db.Services.CountAsync(s => s.IsActive, cancellationToken).ConfigureAwait(false);
        var clients = await db.Clients.CountAsync(c => c.IsActive && c.MergedIntoClientId == null && c.ErasedAt == null, cancellationToken).ConfigureAwait(false);
        var now = clock.GetCurrentInstant();
        var active = AppointmentStateMachine.Active.ToArray();
        var coming = await db.Appointments.CountAsync(a => active.Contains(a.Status) && a.Start >= now, cancellationToken).ConfigureAwait(false);
        var notifications = await settings.LoadAsync<NotificationSettings>(cancellationToken).ConfigureAwait(false);
        var policy = await settings.LoadAsync<CancellationPolicySettings>(cancellationToken).ConfigureAwait(false);
        var privacy = await settings.LoadAsync<PrivacyNoticeSettings>(cancellationToken).ConfigureAwait(false);

        var withoutHours = bookable.Count(s => !s.HasHours);
        return new SetupProgress(
        [
            new(SetupStepKey.SalonDetails, location is { } main && (main.Phone is not null || main.Email is not null), false, []),
            new(SetupStepKey.Team, bookable.Count > 0, false, [bookable.Count]),
            new(SetupStepKey.Hours, bookable.Count > 0 && withoutHours == 0, false, [withoutHours]),
            new(SetupStepKey.PriceList, services > 0, false, [services]),
            new(SetupStepKey.Clients, clients > 0, false, [clients]),
            new(SetupStepKey.Diary, coming > 0, false, [coming]),
            new(SetupStepKey.Reminders, notifications.RemindersEnabled, false, [notifications.FirstReminderHours]),
            new(SetupStepKey.Policy, policy.NoShowFeePercent > 0m || policy.LateCancellationFeePercent > 0m || policy.FreeChangeHours > 0, true, []),

            // Saved once is read once: the date is set the first time the owner saves the page.
            new(SetupStepKey.PrivacyNotice, privacy.UpdatedOn is not null, false, []),
        ]);
    }
}