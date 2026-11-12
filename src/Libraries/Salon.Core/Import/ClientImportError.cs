namespace Salon.Core.Import;

/// <summary>A reason the whole file is refused before any row is looked at (ADR 0019).</summary>
public enum ClientImportError
{
    /// <summary>No header or no data rows.</summary>
    Empty = 1,
    /// <summary>Over <see cref="ClientImportLimits.MaxBytes"/>.</summary>
    TooLarge = 2,
    /// <summary>Over <see cref="ClientImportLimits.MaxRows"/> data rows.</summary>
    TooManyRows = 3,
    /// <summary>No parser recognises the header line.</summary>
    UnknownFormat = 4,
    /// <summary>Binary content (NUL bytes) or an unreadable encoding.</summary>
    NotText = 5,
}
