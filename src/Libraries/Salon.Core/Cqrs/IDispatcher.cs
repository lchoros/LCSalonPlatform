namespace Salon.Core.Cqrs;

/// <summary>
/// Sends a request through the pipeline behaviours to its single handler. Controllers and jobs inject this;
/// they never resolve handlers directly.
/// </summary>
public interface IDispatcher
{
    Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken);
}