using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Catalog;
using Salon.Core.Catalog.Events;
using Salon.Core.Events;
using Salon.Core.Import;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Tenancy;

namespace Salon.Services.Catalog.Import;

internal sealed class ServiceImportService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IEventPublisher publisher,
    ILogger<ServiceImportService> logger) : IServiceImportService
{
    public async Task<ServiceImportPreview> PreviewAsync(ServiceImportParseResult parsed, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!parsed.IsSuccess)
        {
            throw new ArgumentException($"A file refused with {parsed.Error} has nothing to preview.", nameof(parsed));
        }

        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Previewing a price list requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var rows = ServiceImportClassifier.Classify(parsed.Rows, await ExistingAsync(cancellationToken).ConfigureAwait(false));
        return new ServiceImportPreview(tenantId, fileName.Trim(), tenant.Currency, rows);
    }

    public async Task<ServiceImportResult> CommitAsync(ServiceImportPreview preview, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Reading in a price list requires a tenant in scope.");
        if (preview.TenantId != tenantId)
        {
            throw new TenantViolationException("The price list preview belongs to another tenant.");
        }

        // The salon's services as they are now, not as the preview saw them: one added or edited by hand since then
        // is updated by the file, never added a second time. The same rules decide again.
        var rows = preview.Rows
            .Where(r => r.Status is ServiceImportStatus.New or ServiceImportStatus.Changed or ServiceImportStatus.Unchanged)
            .Select(r => r.Row)
            .ToList();
        var now = ServiceImportClassifier.Classify(rows, await ExistingAsync(cancellationToken).ConfigureAwait(false));

        var categories = await db.ServiceCategories.ToListAsync(cancellationToken).ConfigureAwait(false);
        // The unique index on headings is case-sensitive; should a salon have "Hair" and "hair", the first in its order is used.
        var categoryIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var category in categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id))
        {
            categoryIds.TryAdd(CategoryKey(category.Name), category.Id);
        }

        var nextCategoryOrder = categories.Count == 0 ? 1 : categories.Max(c => c.SortOrder) + 1;
        var nextOrder = (await db.Services.MaxAsync(s => (int?)s.SortOrder, cancellationToken).ConfigureAwait(false) ?? 0) + 1;
        var changedIds = now.Where(r => r.Status == ServiceImportStatus.Changed).Select(r => r.Change!.ServiceId).ToArray();
        var toChange = await db.Services.Where(s => changedIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken).ConfigureAwait(false);

        var added = new List<Service>();
        var updated = new List<Service>();
        var newCategories = 0;
        foreach (var row in now)
        {
            if (row.Status == ServiceImportStatus.New)
            {
                Guid? categoryId = null;
                if (row.Row.Category is { } heading)
                {
                    if (!categoryIds.TryGetValue(CategoryKey(heading), out var found))
                    {
                        var category = new ServiceCategory { TenantId = tenantId, Name = heading.Trim(), SortOrder = nextCategoryOrder++ };
                        db.ServiceCategories.Add(category);
                        found = category.Id;
                        categoryIds[CategoryKey(heading)] = found;
                        newCategories++;
                    }

                    categoryId = found;
                }

                var service = new Service
                {
                    TenantId = tenantId,
                    Name = ServiceRules.NormalizeName(row.Row.Name),
                    Duration = Duration.FromMinutes(row.Row.Minutes!.Value),
                    Price = row.Row.Price!.Value,
                    DepositPercent = row.Row.DepositPercent,
                    Currency = preview.Currency,
                    SortOrder = nextOrder++,
                    CategoryId = categoryId,
                };
                db.Services.Add(service);
                added.Add(service);
            }
            else if (row.Status == ServiceImportStatus.Changed && toChange.TryGetValue(row.Change!.ServiceId, out var service))
            {
                // The time, the price and the deposit, and nothing else: the name, the heading, the people who do it
                // and whether it is in use are the service form's to change.
                service.Duration = Duration.FromMinutes(row.Change.MinutesTo);
                service.Price = row.Change.PriceTo;
                service.DepositPercent = row.Change.DepositTo;
                updated.Add(service);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var service in added)
        {
            await publisher.PublishAsync(new ServiceCreated(service.Id, tenantId, service.Name), cancellationToken).ConfigureAwait(false);
        }

        foreach (var service in updated)
        {
            await publisher.PublishAsync(new ServiceUpdated(service.Id, tenantId), cancellationToken).ConfigureAwait(false);
        }

        // Counts and ids only: a file name and service names are text somebody typed.
        var unchanged = now.Count(r => r.Status == ServiceImportStatus.Unchanged);
        var left = now.Count(r => r.Status is ServiceImportStatus.Invalid or ServiceImportStatus.Repeated);
        logger.LogInformation(
            "Price list read in for tenant {TenantId} by user {UserId}: {Added} services added, {Updated} updated, {Unchanged} unchanged, {Categories} headings added, {Left} rows left out",
            tenantId, byUserId ?? Guid.Empty, added.Count, updated.Count, unchanged, newCategories, preview.InvalidCount + preview.RepeatedCount + left);
        return new ServiceImportResult(preview.FileName, added.Count, updated.Count, unchanged, preview.InvalidCount + left, preview.RepeatedCount, newCategories);
    }

    /// <summary>A heading as it is compared: trimmed and without case.</summary>
    private static string CategoryKey(string name) => name.Trim().ToUpperInvariant();

    /// <summary>Every service of the salon by its name, in use or not: a service not in use still owns its name.</summary>
    private async Task<Dictionary<string, ExistingService>> ExistingAsync(CancellationToken cancellationToken)
    {
        var services = await db.Services.AsNoTracking()
            .Select(s => new { s.Id, s.Name, s.Duration, s.Price, s.DepositPercent, s.ProcessingMinutes, s.ProcessingStartsAfterMinutes })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var byName = new Dictionary<string, ExistingService>(StringComparer.Ordinal);
        foreach (var s in services)
        {
            // The unique index on names is case-sensitive; should a salon have "Cut" and "cut", the file updates the first.
            byName.TryAdd(
                ServiceImportClassifier.NameKey(s.Name),
                new ExistingService(s.Id, s.Name, (int)s.Duration.TotalMinutes, s.Price, s.DepositPercent)
                {
                    ProcessingMinutes = s.ProcessingMinutes,
                    ProcessingStartsAfterMinutes = s.ProcessingStartsAfterMinutes,
                });
        }

        return byName;
    }
}