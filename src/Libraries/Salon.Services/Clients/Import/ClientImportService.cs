using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Salon.Core.Clients;
using Salon.Core.Import;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Tenancy;

namespace Salon.Services.Clients.Import;

internal sealed class ClientImportService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClientService clients,
    ILogger<ClientImportService> logger) : IClientImportService
{
    public async Task<ClientImportPreview> PreviewAsync(ClientImportParseResult parsed, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!parsed.IsSuccess || parsed.Format is null)
        {
            throw new ArgumentException($"A file refused with {parsed.Error} has nothing to preview.", nameof(parsed));
        }

        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Previewing a client import requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);

        // Three small queries instead of one per row: a salon has thousands of clients at most, a file 5 000 rows.
        var phones = await db.Clients.AsNoTracking().Where(c => c.Phone != null).Select(c => c.Phone!).ToListAsync(cancellationToken).ConfigureAwait(false);
        var emails = await db.Clients.AsNoTracking().Where(c => c.Email != null).Select(c => c.Email!).ToListAsync(cancellationToken).ConfigureAwait(false);
        var names = await db.Clients.AsNoTracking().Select(c => new { c.FirstName, c.LastName }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var existing = new ExistingClients(
            phones.ToHashSet(StringComparer.Ordinal),
            emails.ToHashSet(StringComparer.Ordinal),
            names.Select(n => ExistingClients.NameKey(n.FirstName, n.LastName)).ToHashSet(StringComparer.Ordinal));

        var rows = ClientImportClassifier.Classify(parsed.Rows, tenant?.Currency, existing);
        return new ClientImportPreview(tenantId, fileName.Trim(), parsed.Format, rows);
    }

    public async Task<ClientImportResult> CommitAsync(ClientImportPreview preview, string consentWording, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentException.ThrowIfNullOrWhiteSpace(consentWording);

        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Committing a client import requires a tenant in scope.");
        if (preview.TenantId != tenantId)
        {
            throw new TenantViolationException("The import preview belongs to another tenant.");
        }

        var created = 0;
        var duplicates = preview.DuplicateCount;
        foreach (var row in preview.Rows.Where(r => r.Status == ImportRowStatus.New))
        {
            var request = new CreateClientRequest(
                row.Row.FirstName,
                row.Row.LastName,
                row.Row.Phone,
                row.Row.Email,
                Truncate(row.Row.Notes, Client.NotesMaxLength),
                HealthNotes: null,
                LastPatchTestOn: null,
                row.Row.MarketingConsent,
                row.Row.MarketingConsent ? Truncate(consentWording, Client.MarketingConsentWordingMaxLength) : null);

            try
            {
                await clients.CreateAsync(request, cancellationToken).ConfigureAwait(false);
                created++;
            }
            catch (DuplicateClientException)
            {
                // Added between preview and confirm (or by an earlier row of this very import); the preview already said what it would do.
                logger.LogInformation("Import row {SourceRow} of {FileName} skipped: the client already exists", row.Row.SourceRow, preview.FileName);
                duplicates++;
            }
        }

        logger.LogInformation("Imported {Created} clients from {FileName} ({Format}); {Duplicates} duplicates, {Invalid} invalid", created, preview.FileName, preview.Format, duplicates, preview.InvalidCount);
        return new ClientImportResult(preview.FileName, preview.Format, created, duplicates, preview.InvalidCount);
    }

    private static string? Truncate(string? text, int maxLength)
        => text is null || text.Length <= maxLength ? text : text[..maxLength].TrimEnd();
}