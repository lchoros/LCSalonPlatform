using Salon.Core.Import;

namespace Salon.Plugin.Import.Csv;

/// <summary>A split CSV file: the header line and the data records that follow it.</summary>
/// <param name="Truncated">True when reading stopped at the record cap; <see cref="Records"/> then holds exactly the cap.</param>
public sealed record CsvDocument(char Separator, IReadOnlyList<string> Header, IReadOnlyList<ImportRecord> Records, bool Truncated);
