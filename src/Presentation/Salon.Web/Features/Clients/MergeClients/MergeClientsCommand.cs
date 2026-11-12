using Salon.Core.Cqrs;

namespace Salon.Web.Features.Clients.MergeClients;

/// <summary>
/// Merges <paramref name="SourceId"/> into <paramref name="TargetId"/> (ADR 0017) inside the pipeline's transaction.
/// Returns the two names for the confirmation, or null when either id is not a client of the current salon. The rules
/// themselves live in <see cref="Salon.Core.Clients.ClientMergeRules"/> and run in the client service, which throws
/// <see cref="Salon.Services.Clients.ClientMergeRefusedException"/> for a self-merge or a card already merged.
/// </summary>
/// <param name="ByUserId">The login that confirmed the merge; logged with the ids.</param>
public sealed record MergeClientsCommand(Guid TargetId, Guid SourceId, Guid? ByUserId) : ICommand<MergeClientsResult?>;

/// <summary>Names as they were before the merge, for the flash on the kept card.</summary>
public sealed record MergeClientsResult(string SourceName, string TargetName);
