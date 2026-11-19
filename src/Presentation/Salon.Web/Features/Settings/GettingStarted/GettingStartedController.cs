using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Settings;
using Salon.Services.Settings;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Infrastructure;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Settings.GettingStarted;

/// <summary>What is set up of a salon and what is left, from its own data (ADR 0068). A query: it reads, never writes.</summary>
public sealed record GettingStartedQuery : IQuery<SetupChecklist>;

/// <param name="State">The step as the salon's data says it stands.</param>
/// <param name="Path">Where the step is done.</param>
/// <param name="ImportPath">Where the same can come over from a file, when the salon may use it.</param>
public sealed record SetupStep(SetupStepState State, string Path, string? ImportPath = null)
{
    public SetupStepKey Key => State.Key;

    public bool Done => State.Done;

    public bool Optional => State.Optional;

    /// <summary>The resource key of the step's sentence for its state.</summary>
    public string SentenceKey => $"Settings.GettingStarted.{Key}.{(Done ? "Done" : "Todo")}";

    /// <summary>The counts as format arguments.</summary>
    public object[] Arguments => [.. State.Counts.Cast<object>()];
}

public sealed record SetupChecklist(IReadOnlyList<SetupStep> Steps)
{
    public int Required => Steps.Count(s => !s.Optional);

    public int RequiredDone => Steps.Count(s => !s.Optional && s.Done);

    public bool AllDone => RequiredDone == Required;
}

/// <summary>
/// Reads through <see cref="ISetupProgressService"/> (the rules live there, shared with the diary's note, ADR 0072)
/// and says where each step is done. The addresses are this slice's own concern.
/// </summary>
public sealed class GettingStartedHandler(
    ISetupProgressService progress,
    IOptionsMonitor<FeaturesOptions> features) : IRequestHandler<GettingStartedQuery, SetupChecklist>
{
    public async Task<SetupChecklist> HandleAsync(GettingStartedQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var steps = (await progress.GetAsync(cancellationToken)).Steps;
        var imports = features.CurrentValue.ClientImport;
        return new SetupChecklist([.. steps.Select(s => new SetupStep(s, Path(s.Key), ImportPath(s.Key, imports)))]);
    }

    private static string Path(SetupStepKey key) => key switch
    {
        SetupStepKey.SalonDetails => GettingStartedController.SalonDetailsPath,
        SetupStepKey.Team or SetupStepKey.Hours => GettingStartedController.StaffPath,
        SetupStepKey.PriceList => GettingStartedController.ServicesPath,
        SetupStepKey.Clients => GettingStartedController.ClientsPath,
        SetupStepKey.Diary => GettingStartedController.DiaryPath,
        SetupStepKey.Reminders => GettingStartedController.NotificationsPath,
        SetupStepKey.Policy => GettingStartedController.PolicyPath,
        SetupStepKey.PrivacyNotice => GettingStartedController.PrivacyPath,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    private static string? ImportPath(SetupStepKey key, bool imports) => key switch
    {
        SetupStepKey.PriceList => GettingStartedController.ServiceImportPath,
        SetupStepKey.Clients when imports => GettingStartedController.ClientImportPath,
        SetupStepKey.Diary when imports => GettingStartedController.DiaryImportPath,
        _ => null,
    };
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