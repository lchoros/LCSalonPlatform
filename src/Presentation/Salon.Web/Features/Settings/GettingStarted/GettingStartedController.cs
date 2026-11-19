using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Settings;
using Salon.Data;
using Salon.Services.Locations;
using Salon.Services.Settings;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Infrastructure;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Settings.GettingStarted;

/// <summary>What is set up of a salon and what is left, from its own data (ADR 0068). A query: it reads, never writes.</summary>
public sealed record GettingStartedQuery : IQuery<SetupChecklist>;

/// <summary>A step of setting a salon up.</summary>
public enum SetupStepKey
{
    SalonDetails = 1,
    Team = 2,
    Hours = 3,
    PriceList = 4,
    Clients = 5,
    Diary = 6,
    Reminders = 7,
    Policy = 8,
    PrivacyNotice = 9,
}

/// <param name="Done">What the salon's data says, not a box somebody ticked.</param>
/// <param name="Optional">A step a salon may leave: it is not counted toward "all done".</param>
/// <param name="Counts">The numbers the step's sentence names, in order.</param>
/// <param name="Path">Where the step is done.</param>
/// <param name="ImportPath">Where the same can come over from a file, when the salon may use it.</param>
public sealed record SetupStep(SetupStepKey Key, bool Done, bool Optional, IReadOnlyList<int> Counts, string Path, string? ImportPath = null)
{
    /// <summary>The resource key of the step's sentence for its state.</summary>
    public string SentenceKey => $"Settings.GettingStarted.{Key}.{(Done ? "Done" : "Todo")}";

    /// <summary>The counts as format arguments.</summary>
    public object[] Arguments => [.. Counts.Cast<object>()];
}

public sealed record SetupChecklist(IReadOnlyList<SetupStep> Steps)
{
    public int Required => Steps.Count(s => !s.Optional);

    public int RequiredDone => Steps.Count(s => !s.Optional && s.Done);

    public bool AllDone => RequiredDone == Required;
}

/// <summary>
/// Reads through the tenant filter only: a handful of counts. Each step is "done" when the salon's data says so, so
/// the list is never out of date and needs no box of its own.
/// </summary>
public sealed class GettingStartedHandler(
    SalonDbContext db,
    ILocationService locations,
    ISettingService settings,
    IOptionsMonitor<FeaturesOptions> features,
    IClock clock) : IRequestHandler<GettingStartedQuery, SetupChecklist>
{
    public async Task<SetupChecklist> HandleAsync(GettingStartedQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var location = await locations.GetMainAsync(cancellationToken);
        var bookable = await db.StaffMembers.AsNoTracking()
            .Where(s => s.IsActive && s.IsBookable)
            .Select(s => new { s.Id, HasHours = db.WorkingHours.Any(w => w.StaffMemberId == s.Id) })
            .ToListAsync(cancellationToken);
        var services = await db.Services.CountAsync(s => s.IsActive, cancellationToken);
        var clients = await db.Clients.CountAsync(c => c.IsActive && c.MergedIntoClientId == null && c.ErasedAt == null, cancellationToken);
        var now = clock.GetCurrentInstant();
        var active = AppointmentStateMachine.Active.ToArray();
        var coming = await db.Appointments.CountAsync(a => active.Contains(a.Status) && a.Start >= now, cancellationToken);
        var notifications = await settings.LoadAsync<NotificationSettings>(cancellationToken);
        var policy = await settings.LoadAsync<CancellationPolicySettings>(cancellationToken);
        var privacy = await settings.LoadAsync<PrivacyNoticeSettings>(cancellationToken);
        var imports = features.CurrentValue.ClientImport;

        var withoutHours = bookable.Count(s => !s.HasHours);
        return new SetupChecklist(
        [
            new(SetupStepKey.SalonDetails, location is { } main && (main.Phone is not null || main.Email is not null), false, [], GettingStartedController.SalonDetailsPath),
            new(SetupStepKey.Team, bookable.Count > 0, false, [bookable.Count], GettingStartedController.StaffPath),
            new(SetupStepKey.Hours, bookable.Count > 0 && withoutHours == 0, false, [withoutHours], GettingStartedController.StaffPath),
            new(SetupStepKey.PriceList, services > 0, false, [services], GettingStartedController.ServicesPath, GettingStartedController.ServiceImportPath),
            new(SetupStepKey.Clients, clients > 0, false, [clients], GettingStartedController.ClientsPath, imports ? GettingStartedController.ClientImportPath : null),
            new(SetupStepKey.Diary, coming > 0, false, [coming], GettingStartedController.DiaryPath, imports ? GettingStartedController.DiaryImportPath : null),
            new(SetupStepKey.Reminders, notifications.RemindersEnabled, false, [notifications.FirstReminderHours], GettingStartedController.NotificationsPath),
            new(SetupStepKey.Policy, policy.NoShowFeePercent > 0m || policy.LateCancellationFeePercent > 0m || policy.FreeChangeHours > 0, true, [], GettingStartedController.PolicyPath),

            // Saved once is read once: the date is set the first time the owner saves the page.
            new(SetupStepKey.PrivacyNotice, privacy.UpdatedOn is not null, false, [], GettingStartedController.PrivacyPath),
        ]);
    }
}

/// <summary>
/// <c>/admin/settings/getting-started</c> (ADR 0068): one page with what is set up of the salon and what is left,
/// each step with a link to where it is done. Read from the salon's own data, so it is never out of date. Owner
/// only (<see cref="Permissions.SettingsManage"/>), like the other settings.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.SettingsManage)]
[Route("admin/settings/getting-started")]
public sealed class GettingStartedController(IDispatcher dispatcher) : SalonControllerBase
{
    // The pages the steps link to; each is owned by its own slice (ADR 0043: addresses are built here, not in the view).
    public const string SalonDetailsPath = "/admin/settings/salon-details";
    public const string StaffPath = "/admin/staff";
    public const string ServicesPath = "/admin/services";
    public const string ServiceImportPath = "/admin/services/import";
    public const string ClientsPath = "/admin/clients";
    public const string ClientImportPath = "/admin/clients/import";
    public const string DiaryPath = "/admin/appointments";
    public const string DiaryImportPath = "/admin/appointments/import";
    public const string NotificationsPath = "/admin/settings/notifications";
    public const string PolicyPath = "/admin/settings/cancellation-policy";
    public const string PrivacyPath = "/admin/settings/privacy";

    /// <summary>The salon's booking page as a client sees it.</summary>
    public const string BookingPagePath = "/book";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(await dispatcher.SendAsync(new GettingStartedQuery(), cancellationToken));
}