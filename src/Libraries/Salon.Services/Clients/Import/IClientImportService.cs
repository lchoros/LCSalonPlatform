using Salon.Core.Import;

namespace Salon.Services.Clients.Import;

/// <summary>
/// Preview-then-commit import of parsed client rows into the current salon (ADR 0019). <see cref="PreviewAsync"/> only
/// reads; <see cref="CommitAsync"/> creates the rows the preview classified as <see cref="ImportRowStatus.New"/> through
/// <see cref="IClientService.CreateAsync"/>, so normalisation, duplicate checks and the ClientCreated event are the form's.
/// </summary>
public interface IClientImportService
{
    /// <summary>Classifies every row as new, duplicate or invalid for the current tenant. Requires a tenant in scope.</summary>
    Task<ClientImportPreview> PreviewAsync(ClientImportParseResult parsed, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the new rows of <paramref name="preview"/>; must run inside a command (one transaction). A row that became a
    /// duplicate since the preview is counted, not thrown. <paramref name="consentWording"/> is recorded for rows with consent.
    /// </summary>
    Task<ClientImportResult> CommitAsync(ClientImportPreview preview, string consentWording, CancellationToken cancellationToken = default);
}

public enum ImportRowStatus
{
    New = 1,
    Duplicate = 2,
    Invalid = 3,
}

/// <summary>A row after classification; <see cref="Problems"/> is the parser's list plus what the classifier found.</summary>
public sealed record ClassifiedImportRow(
    ImportedClientRow Row,
    ImportRowStatus Status,
    IReadOnlyList<ImportProblem> Problems,
    string? NormalisedPhone,
    string? NormalisedEmail)
{
    public override string ToString() => $"{nameof(ClassifiedImportRow)} {{ SourceRow = {Row.SourceRow}, Status = {Status} }}";
}

/// <summary>What the owner confirms: the classified rows of one file for one salon. Cached server-side, never re-posted.</summary>
public sealed record ClientImportPreview(Guid TenantId, string FileName, string Format, IReadOnlyList<ClassifiedImportRow> Rows)
{
    public int NewCount => Rows.Count(r => r.Status == ImportRowStatus.New);

    public int DuplicateCount => Rows.Count(r => r.Status == ImportRowStatus.Duplicate);

    public int InvalidCount => Rows.Count(r => r.Status == ImportRowStatus.Invalid);

    public override string ToString() => $"{nameof(ClientImportPreview)} {{ TenantId = {TenantId}, Rows = {Rows.Count} }}";
}

/// <summary>Counts after the commit. <see cref="Duplicates"/> includes rows that became duplicates between preview and confirm.</summary>
public sealed record ClientImportResult(string FileName, string Format, int Created, int Duplicates, int Invalid);