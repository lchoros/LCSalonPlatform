using Salon.Core.Import;
using Salon.Services.Clients.Import;

namespace Salon.Services.Products.Import;

/// <summary>
/// Preview-then-commit import of a product list into the current salon (ADR 0045), in the shape of the client import
/// (ADR 0019). <see cref="PreviewAsync"/> only reads. <see cref="CommitAsync"/> adds the rows the preview called new,
/// each with its opening count as a line of the stock history. A product the salon already has is never changed.
/// </summary>
public interface IProductImportService
{
    /// <summary>Classifies every row as new, already there or invalid for the current tenant. Requires a tenant in scope.</summary>
    Task<ProductImportPreview> PreviewAsync(ProductImportParseResult parsed, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the new rows of <paramref name="preview"/>; must run inside a command (one transaction). A row whose name
    /// was taken since the preview is counted, not thrown. <paramref name="stockNote"/> goes on every opening count so
    /// the history says where the number came from.
    /// </summary>
    Task<ProductImportResult> CommitAsync(ProductImportPreview preview, string stockNote, Guid? byUserId, CancellationToken cancellationToken = default);
}

/// <summary>A row after classification; <see cref="Problems"/> is the reader's list plus what the classifier found.</summary>
public sealed record ClassifiedProductRow(ImportedProductRow Row, ImportRowStatus Status, IReadOnlyList<ProductImportProblem> Problems);

/// <summary>What the owner confirms: the classified rows of one file for one salon. Kept server-side, never posted back.</summary>
/// <param name="Currency">The salon's currency, which every price of the file is read in.</param>
public sealed record ProductImportPreview(Guid TenantId, string FileName, string Currency, IReadOnlyList<ClassifiedProductRow> Rows)
{
    public int NewCount => Rows.Count(r => r.Status == ImportRowStatus.New);

    public int DuplicateCount => Rows.Count(r => r.Status == ImportRowStatus.Duplicate);

    public int InvalidCount => Rows.Count(r => r.Status == ImportRowStatus.Invalid);
}

/// <summary>Counts after the commit.</summary>
/// <param name="Duplicates">Includes rows whose name was taken between preview and confirm.</param>
/// <param name="Units">Units put on the shelves by the opening counts.</param>
public sealed record ProductImportResult(string FileName, int Created, int Duplicates, int Invalid, int Units);