using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Cqrs;

namespace Salon.Web.Framework.Pipeline;

/// <summary>
/// In-house request dispatcher (see ADR 0003). Resolves the single <see cref="IRequestHandler{TRequest, TResult}"/>
/// from the current scope and wraps it in every registered <see cref="IPipelineBehavior{TRequest, TResult}"/>,
/// outermost first in registration order. One generic wrapper is built per request type and cached for the process.
/// </summary>
public sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    private static readonly ConcurrentDictionary<(Type Request, Type Result), object> Wrappers = new();

    public Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wrapper = (RequestHandlerWrapper<TResult>)Wrappers.GetOrAdd(
            (request.GetType(), typeof(TResult)),
            static key => Activator.CreateInstance(typeof(RequestHandlerWrapper<,>).MakeGenericType(key.Request, key.Result))!);

        return wrapper.HandleAsync(request, serviceProvider, cancellationToken);
    }

    private abstract class RequestHandlerWrapper<TResult>
    {
        public abstract Task<TResult> HandleAsync(IRequest<TResult> request, IServiceProvider provider, CancellationToken cancellationToken);
    }

    private sealed class RequestHandlerWrapper<TRequest, TResult> : RequestHandlerWrapper<TResult>
        where TRequest : IRequest<TResult>
    {
        public override Task<TResult> HandleAsync(IRequest<TResult> request, IServiceProvider provider, CancellationToken cancellationToken)
        {
            var handlers = provider.GetServices<IRequestHandler<TRequest, TResult>>().ToList();
            var handler = handlers.Count switch
            {
                1 => handlers[0],
                0 => throw new InvalidOperationException(
                    $"No handler is registered for request '{typeof(TRequest).Name}'. "
                    + $"Add a class implementing IRequestHandler<{typeof(TRequest).Name}, {typeof(TResult).Name}> to the slice "
                    + "and make sure its assembly is passed to AddSalonPipeline."),
                _ => throw new InvalidOperationException(
                    $"Request '{typeof(TRequest).Name}' has {handlers.Count} handlers ({string.Join(", ", handlers.Select(h => h.GetType().Name))}); exactly one is allowed (ADR 0003)."),
            };

            var typedRequest = (TRequest)request;
            RequestHandlerDelegate<TResult> pipeline = () => handler.HandleAsync(typedRequest, cancellationToken);

            var behaviors = provider.GetServices<IPipelineBehavior<TRequest, TResult>>().ToList();
            for (var i = behaviors.Count - 1; i >= 0; i--)
            {
                var behavior = behaviors[i];
                var next = pipeline;
                pipeline = () => behavior.HandleAsync(typedRequest, next, cancellationToken);
            }

            return pipeline();
        }
    }
}