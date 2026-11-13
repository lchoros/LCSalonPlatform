using Salon.Core.Plugins;

namespace Salon.Core.Import;

/// <summary>
/// The import plugin (ADR 0019): turns the bytes of an uploaded file into <see cref="ImportedClientRow"/>s by decoding,
/// splitting and detecting the source product from the header line. Enforces <see cref="ClientImportLimits"/>.
/// Pure: nothing here touches the database or the tenant.
/// </summary>
public interface IClientImportProvider : IPlugin
{
    /// <summary>The products this provider can read, by <see cref="IClientImportParser.Format"/>.</summary>
    IReadOnlyList<string> Formats { get; }

    ClientImportParseResult Parse(ReadOnlyMemory<byte> content);
}