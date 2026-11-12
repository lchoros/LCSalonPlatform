namespace Salon.Core.Import;

/// <summary>
/// Knows one source product's client export (ADR 0019). Pure: it works on records already decoded and split by the
/// provider, so it can be unit-tested without files. The provider asks every parser to <see cref="Match"/> the header and
/// hands the records to the one that recognises the most columns.
/// </summary>
public interface IClientImportParser
{
    /// <summary>Stable product name shown in the preview and recorded with imported consent, e.g. "Booksy".</summary>
    string Format { get; }

    /// <summary>How many header columns this parser recognises; 0 when the file cannot be this product (no name or contact column).</summary>
    int Match(IReadOnlyList<string> header);

    /// <summary>Maps every record to a row. Never throws for bad data: problems are reported on the row.</summary>
    IReadOnlyList<ImportedClientRow> Parse(IReadOnlyList<string> header, IReadOnlyList<ImportRecord> records);
}
