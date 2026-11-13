using Salon.Core.Cqrs;
using Salon.Services.Clients.Import;

namespace Salon.Web.Features.Clients.ImportClients;

/// <summary>
/// Step 2 → 3 of the import (ADR 0019): create the rows the preview classified as new. A command, so the pipeline wraps
/// every created client and its ClientCreated outbox row in one transaction. The preview comes from the server-side
/// session the controller looked up by token, never from the browser.
/// </summary>
public sealed record ConfirmClientImportCommand(ClientImportPreview Preview) : ICommand<ClientImportResult>
{
    public override string ToString() => $"{nameof(ConfirmClientImportCommand)} {{ FileName = {Preview.FileName}, New = {Preview.NewCount} }}";
}