namespace Salon.Core.Cqrs;

/// <summary>Handles exactly one request type. One handler per request; registered by assembly scan as scoped.</summary>
public interface IRequestHandler<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<TResult> HandleAsync(TRequest request, CancellationToken cancellationToken);
}