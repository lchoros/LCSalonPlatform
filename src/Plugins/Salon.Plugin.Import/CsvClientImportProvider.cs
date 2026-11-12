using Salon.Core.Import;

namespace Salon.Plugin.Import;

/// <summary>The import plugin (ADR 0019): decode, split, cap, detect the product, map rows.</summary>
public sealed class CsvClientImportProvider(IEnumerable<IClientImportParser> parsers) : IClientImportProvider
{
    public const string PluginSystemName = "Import.Csv";

    private readonly IReadOnlyList<IClientImportParser> _parsers = parsers.ToList();

    public string SystemName => PluginSystemName;

    public string DisplayNameKey => "Plugins.Import.Csv.DisplayName";

    public IReadOnlyList<string> Formats => _parsers.Select(p => p.Format).ToList();

    public ClientImportParseResult Parse(ReadOnlyMemory<byte> content) => throw new NotImplementedException();
}
