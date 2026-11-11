using System.Diagnostics.CodeAnalysis;

namespace Salon.Core.Cqrs;

/// <summary>Continues the pipeline: the next behaviour, or the handler itself at the end.</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "It is a delegate; the conventional CQRS name keeps slices readable next to the usual pipeline samples.")]
public delegate Task<TResult> RequestHandlerDelegate<TResult>();

/// <summary>
/// Cross-cutting step around every handler (logging, validation, tenant guard, transaction).
/// Behaviours run in registration order; each decides whether to call <c>next</c>.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "'next' is the conventional name for the continuation; the solution is C# only.")]
    Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken);
}