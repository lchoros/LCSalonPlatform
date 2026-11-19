namespace Salon.Core.Import;

/// <summary>Why a row of a price list cannot be read in as it is (ADR 0066).</summary>
public enum ServiceImportProblem
{
    /// <summary>The record has a different number of fields than the header.</summary>
    WrongColumnCount = 1,
    MissingName = 2,
    NameTooLong = 3,
    CategoryTooLong = 4,

    /// <summary>Not a whole number of minutes, outside what a visit can last, or not on the five-minute step.</summary>
    InvalidMinutes = 5,

    /// <summary>Not a price, below zero, more than two decimals or over the cap.</summary>
    InvalidPrice = 6,

    /// <summary>Not a percentage from 0 to 100.</summary>
    InvalidDeposit = 7,

    /// <summary>A service the salon does not have yet needs its time.</summary>
    MissingMinutes = 8,

    /// <summary>A service the salon does not have yet needs its price.</summary>
    MissingPrice = 9,

    /// <summary>An earlier row of the same file has the same name.</summary>
    RepeatedInFile = 10,

    /// <summary>The variant is longer than its cap, or the name beside a variant is longer than a group may be.</summary>
    VariantTooLong = 11,
}

/// <summary>
/// One service as it appears in a price list file (ADR 0066): text trimmed, numbers read. A number that could not be
/// read is null and named in <see cref="Problems"/>; the classifier adds what needs the salon's context.
/// </summary>
/// <param name="SourceRow">1-based line number of the record in the file, header included.</param>
/// <param name="Variant">What tells this service from its siblings under one name, e.g. "Long hair"; null when the file gives none.</param>
/// <param name="Category">The heading the service is listed under; null when the file gives none.</param>
/// <param name="Minutes">How long the visit takes; null when the file gives none.</param>
/// <param name="Price">What a client pays; null when the file gives none.</param>
/// <param name="DepositPercent">The deposit as a percentage of the price; null when the file gives none.</param>
public sealed record ImportedServiceRow(
    int SourceRow,
    string Name,
    string? Variant,
    string? Category,
    int? Minutes,
    decimal? Price,
    decimal? DepositPercent,
    IReadOnlyList<ServiceImportProblem> Problems)
{
    /// <summary>The service as every screen names it: "Keratin · Long hair" for a variant, the name alone otherwise.</summary>
    public string FullName => Variant is null ? Name : Name + Catalog.ServiceRules.VariantSeparator + Variant;
}

/// <summary>What the provider made of a file: rows, or an <see cref="Error"/> and no rows. The reasons a whole file is refused are those of a client file.</summary>
public sealed record ServiceImportParseResult(IReadOnlyList<string> Header, IReadOnlyList<ImportedServiceRow> Rows, ClientImportError? Error)
{
    public bool IsSuccess => Error is null;

    public static ServiceImportParseResult Failed(ClientImportError error, IReadOnlyList<string>? header = null) => new(header ?? [], [], error);
}

/// <summary>
/// Turns the bytes of an uploaded file into <see cref="ImportedServiceRow"/>s (ADR 0066): decodes, splits and finds
/// the columns by their headings. Pure: nothing here touches the database or the tenant.
/// </summary>
public interface IServiceImportProvider
{
    ServiceImportParseResult Parse(ReadOnlyMemory<byte> content);
}

/// <summary>Caps on one price list file (ADR 0066); the byte cap is <see cref="ClientImportLimits.MaxBytes"/>.</summary>
public static class ServiceImportLimits
{
    /// <summary>More than any salon's price list; keeps one confirm inside one short transaction.</summary>
    public const int MaxRows = 1_000;
}