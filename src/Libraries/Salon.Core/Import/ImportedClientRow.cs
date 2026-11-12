namespace Salon.Core.Import;

/// <summary>
/// One client as it appears in an import file (ADR 0019): text as typed, trimmed, before any normalisation. Health data is
/// never part of an import; free text lands in <see cref="Notes"/> only. <see cref="Problems"/> lists what the parser could
/// already tell is wrong with the row; the classifier adds what needs the salon's context (country, existing clients).
/// </summary>
/// <param name="SourceRow">1-based line number of the record in the file, header included, for the preview and messages.</param>
public sealed record ImportedClientRow(
    int SourceRow,
    string FirstName,
    string LastName,
    string? Phone,
    string? Email,
    string? Notes,
    bool MarketingConsent,
    IReadOnlyList<ImportProblem> Problems)
{
    /// <summary>Keeps contact details out of any log line that prints the row.</summary>
    public override string ToString() => $"{nameof(ImportedClientRow)} {{ SourceRow = {SourceRow} }}";
}
