using Salon.Core.Import;
using Salon.Plugin.Import.Csv;

namespace Salon.Plugin.Import;

/// <summary>
/// The import plugin (ADR 0019): refuses an oversized or binary file, decodes, splits with the row cap, and hands the
/// records to the parser whose <see cref="IClientImportParser.Match"/> is highest (ties keep registration order, so a
/// Polish comma-separated file with the shared name columns goes to the first parser). Pure: no database, no tenant.
/// </summary>
public sealed class CsvClientImportProvider(IEnumerable<IClientImportParser> parsers) : IClientImportProvider
{
    public const string PluginSystemName = "Import.Csv";

    private readonly IReadOnlyList<IClientImportParser> _parsers = parsers.ToList();

    public string SystemName => PluginSystemName;

    public string DisplayNameKey => "Plugins.Import.Csv.DisplayName";

    public IReadOnlyList<string> Formats => _parsers.Select(p => p.Format).ToList();

    public ClientImportParseResult Parse(ReadOnlyMemory<byte> content)
    {
        if (content.Length > ClientImportLimits.MaxBytes)
        {
            return ClientImportParseResult.Failed(ClientImportError.TooLarge);
        }

        if (!CsvDecoder.TryDecode(content.Span, out var text))
        {
            return ClientImportParseResult.Failed(ClientImportError.NotText);
        }

        var document = CsvReader.Read(text, ClientImportLimits.MaxRows);
        if (document.Header.Count == 0 || document.Records.Count == 0)
        {
            return ClientImportParseResult.Failed(ClientImportError.Empty, document.Header);
        }

        if (document.Truncated)
        {
            return ClientImportParseResult.Failed(ClientImportError.TooManyRows, document.Header);
        }

        var parser = _parsers
            .Select(p => (Parser: p, Score: p.Match(document.Header)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Parser)
            .FirstOrDefault();
        if (parser is null)
        {
            return ClientImportParseResult.Failed(ClientImportError.UnknownFormat, document.Header);
        }

        return new ClientImportParseResult(parser.Format, document.Header, parser.Parse(document.Header, document.Records), null);
    }
}