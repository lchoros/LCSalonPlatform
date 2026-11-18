using Salon.Core.Import;

namespace Salon.Services.Catalog.Import;

/// <summary>
/// Preview-then-commit of a price list file into the current salon (ADR 0066), in the shape of the product import
/// (ADR 0045) with one thing more: a service the salon already has is updated. <see cref="PreviewAsync"/> only reads
/// and says, row by row, what the file would add and what it would change. <see cref="CommitAsync"/> does exactly
/// that: adds the new services under their headings and gives the others the file's time, price and deposit. Nothing
/// is ever removed, and a service the file does not mention is left alone.
/// </summary>
public interface IServiceImportService
{
    /// <summary>Classifies every row for the current tenant. Requires a tenant in scope.</summary>
    Task<ServiceImportPreview> PreviewAsync(ServiceImportParseResult parsed, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="preview"/>; must run inside a command (one transaction). A row whose name was taken
    /// since the preview updates that service instead of adding a second one.
    /// </summary>
    Task<ServiceImportResult> CommitAsync(ServiceImportPreview preview, Guid? byUserId, CancellationToken cancellationToken = default);
}

public enum ServiceImportStatus
{
    /// <summary>The salon has no service of this name: it is added.</summary>
    New = 1,

    /// <summary>The salon has it and the file gives another time, price or deposit: it is updated.</summary>
    Changed = 2,

    /// <summary>The salon has it and the file says nothing different.</summary>
    Unchanged = 3,

    /// <summary>The row cannot be read in; the reasons are listed.</summary>
    Invalid = 4,

    /// <summary>An earlier row of the file has the same name; this one is left out.</summary>
    Repeated = 5,
}

/// <summary>What a row would change on a service the salon has: each value as it is and as the file gives it.</summary>
/// <param name="DepositFrom">Null follows the salon's own rule.</param>
public sealed record ServiceChange(Guid ServiceId, int MinutesFrom, int MinutesTo, decimal PriceFrom, decimal PriceTo, decimal? DepositFrom, decimal? DepositTo)
{
    public bool MinutesChange => MinutesFrom != MinutesTo;

    public bool PriceChanges => PriceFrom != PriceTo;

    public bool DepositChanges => DepositFrom != DepositTo;

    public bool Any => MinutesChange || PriceChanges || DepositChanges;
}

/// <summary>A row after classification; <see cref="Problems"/> is the reader's list plus what the classifier found.</summary>
/// <param name="Change">For a service the salon has: what the file says against what is there. Null for the rest.</param>
public sealed record ClassifiedServiceRow(ImportedServiceRow Row, ServiceImportStatus Status, IReadOnlyList<ServiceImportProblem> Problems, ServiceChange? Change = null);

/// <summary>What the owner confirms: the classified rows of one file for one salon. Kept server-side, never posted back.</summary>
/// <param name="Currency">The salon's currency, which every price of the file is read in.</param>
public sealed record ServiceImportPreview(Guid TenantId, string FileName, string Currency, IReadOnlyList<ClassifiedServiceRow> Rows)
{
    public int NewCount => Count(ServiceImportStatus.New);

    public int ChangedCount => Count(ServiceImportStatus.Changed);

    public int UnchangedCount => Count(ServiceImportStatus.Unchanged);

    public int InvalidCount => Count(ServiceImportStatus.Invalid);

    public int RepeatedCount => Count(ServiceImportStatus.Repeated);

    /// <summary>True when confirming would write something.</summary>
    public bool HasWork => NewCount + ChangedCount > 0;

    private int Count(ServiceImportStatus status) => Rows.Count(r => r.Status == status);
}

/// <summary>Counts after the commit.</summary>
/// <param name="Categories">Headings the file named that the salon did not have; they were added with their services.</param>
public sealed record ServiceImportResult(string FileName, int Added, int Updated, int Unchanged, int Invalid, int Repeated, int Categories)
{
    /// <summary>Rows that were not read in: not readable, or said twice in the file.</summary>
    public int LeftOut => Invalid + Repeated;
}