namespace Salon.Core.Import;

/// <summary>
/// What <see cref="IClientImportProvider.Parse"/> made of a file: either <see cref="Rows"/> from the parser named by
/// <see cref="Format"/>, or an <see cref="Error"/> and no rows. <see cref="Header"/> is kept so an unknown format can show
/// what was found.
/// </summary>
public sealed record ClientImportParseResult(
    string? Format,
    IReadOnlyList<string> Header,
    IReadOnlyList<ImportedClientRow> Rows,
    ClientImportError? Error)
{
    public bool IsSuccess => Error is null;

    public static ClientImportParseResult Failed(ClientImportError error, IReadOnlyList<string>? header = null)
        => new(null, header ?? [], [], error);

    /// <summary>Row count only: the rows carry personal data.</summary>
    public override string ToString() => $"{nameof(ClientImportParseResult)} {{ Format = {Format}, Rows = {Rows.Count}, Error = {Error} }}";
}