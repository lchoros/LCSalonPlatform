using Salon.Core.Import;

namespace Salon.Plugin.Import.Parsers;

/// <summary>Versum client export (ADR 0019 lists the assumed columns).</summary>
public sealed class VersumCsvParser : IClientImportParser
{
    public const string FormatName = "Versum";

    public string Format => FormatName;

    public int Match(IReadOnlyList<string> header) => throw new NotImplementedException();

    public IReadOnlyList<ImportedClientRow> Parse(IReadOnlyList<string> header, IReadOnlyList<ImportRecord> records) => throw new NotImplementedException();
}
