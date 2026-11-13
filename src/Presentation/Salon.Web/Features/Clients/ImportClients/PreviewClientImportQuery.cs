using Salon.Core.Cqrs;
using Salon.Core.Import;
using Salon.Services.Clients.Import;

namespace Salon.Web.Features.Clients.ImportClients;

/// <summary>
/// Step 1 → 2 of the import (ADR 0019): parse the uploaded bytes with the import plugin and classify the rows against the
/// current salon. Reads only; the preview is kept server-side by the controller until the owner confirms.
/// </summary>
/// <param name="Content">The file as uploaded; the validator and the provider both enforce <see cref="ClientImportLimits"/>.</param>
/// <param name="FileName">Shown in the preview and recorded as the consent source.</param>
public sealed record PreviewClientImportQuery(ReadOnlyMemory<byte> Content, string FileName) : IQuery<PreviewClientImportOutcome>
{
    /// <summary>Size only: the bytes are a client list.</summary>
    public override string ToString() => $"{nameof(PreviewClientImportQuery)} {{ FileName = {FileName}, Bytes = {Content.Length} }}";
}

/// <summary>Either a file-level <see cref="Error"/> (with the header found, for the message) or the classified <see cref="Preview"/>.</summary>
public sealed record PreviewClientImportOutcome(ClientImportError? Error, IReadOnlyList<string> Header, ClientImportPreview? Preview);