namespace Salon.Core.Import;

/// <summary>One split data record of an import file with the 1-based line it starts on (the header is line 1).</summary>
public sealed record ImportRecord(int SourceRow, IReadOnlyList<string> Fields)
{
    public override string ToString() => $"{nameof(ImportRecord)} {{ SourceRow = {SourceRow}, Fields = {Fields.Count} }}";
}