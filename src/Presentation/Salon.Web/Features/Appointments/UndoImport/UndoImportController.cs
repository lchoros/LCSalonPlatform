using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Services.Appointments.Import;
using Salon.Services.Localization;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Appointments.UndoImport;

/// <summary>The files read into the diary, latest first (ADR 0074). A query: it reads, never writes.</summary>
public sealed record ListDiaryImportsQuery : IQuery<DiaryImportList>;

public sealed record DiaryImportList(IReadOnlyList<DiaryImportLine> Batches);

/// <summary>One read-in file, its moment already in the salon's zone.</summary>
public sealed record DiaryImportLine(Guid Id, LocalDate Day, LocalTime Time, int Brought, int Untouched);

/// <summary>Takes one read-in file out of the diary (ADR 0074). A command: one transaction around the one delete.</summary>
public sealed record UndoDiaryImportCommand(Guid BatchId, Guid? ByUserId) : ICommand<DiaryImportUndo?>;

public sealed class ListDiaryImportsHandler(
    IAppointmentImportService imports,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup) : IRequestHandler<ListDiaryImportsQuery, DiaryImportList>
{
    public async Task<DiaryImportList> HandleAsync(ListDiaryImportsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(ListDiaryImportsQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        var batches = await imports.BatchesAsync(cancellationToken);
        return new DiaryImportList([.. batches.Select(b =>
        {
            var at = b.ReadAt.InZone(zone);
            return new DiaryImportLine(b.Id, at.Date, at.TimeOfDay, b.Brought, b.Untouched);
        })]);
    }
}

public sealed class UndoDiaryImportHandler(IAppointmentImportService imports) : IRequestHandler<UndoDiaryImportCommand, DiaryImportUndo?>
{
    public async Task<DiaryImportUndo?> HandleAsync(UndoDiaryImportCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await imports.UndoAsync(request.BatchId, request.ByUserId, cancellationToken);
    }
}

/// <param name="Flash">Confirmation left by the undo (post-redirect-get), if any.</param>
public sealed record UndoImportViewModel(DiaryImportList List, string? Flash);

/// <summary>
/// <c>/admin/appointments/import/undo</c> (ADR 0074): the files read into the diary and, for each, the way to take
/// it out again. Behind the same permissions as the import itself; the visits the desk has touched since stay, so
/// nothing a client already lived through disappears. Tenant only; another salon's read-in looks like a missing one.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.BookingsManage)]
[RequirePermission(Permissions.ClientsView)]
[RequirePermission(Permissions.ClientsManage)]
[Route("admin/appointments/import/undo")]
public sealed class UndoImportController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string UndoPath = "/admin/appointments/import/undo";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new UndoImportViewModel(await dispatcher.SendAsync(new ListDiaryImportsQuery(), cancellationToken), TakeFlash()));

    [HttpPost("{batchId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Undo(Guid batchId, CancellationToken cancellationToken)
    {
        var undo = await dispatcher.SendAsync(new UndoDiaryImportCommand(batchId, CurrentUserId()), cancellationToken);
        if (undo is null)
        {
            // Another salon's read-in looks the same as a missing one (tenant filter).
            return NotFound();
        }

        SetFlash(localizer.Format("Appointments.Import.Undo.Done", localizer.Plural("Appointments.Import.Undo.Removed", undo.Removed), undo.Kept));
        return Redirect(UndoPath);
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}