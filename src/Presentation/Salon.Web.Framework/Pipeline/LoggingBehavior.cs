using System.Diagnostics;

using Microsoft.Extensions.Logging;

using Salon.Core.Cqrs;

namespace Salon.Web.Framework.Pipeline;

/// <summary>Outermost behaviour: request name and elapsed time at Information, failures at Warning (then rethrown).</summary>
public sealed class LoggingBehavior<TRequest, TResult>(ILogger<LoggingBehavior<TRequest, TResult>> logger)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var requestName = typeof(TRequest).Name;
        var started = Stopwatch.GetTimestamp();

        try
        {
            var result = await next();
            logger.LogInformation("Handled {Request} in {ElapsedMs} ms", requestName, ElapsedMs(started));
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Request} failed after {ElapsedMs} ms", requestName, ElapsedMs(started));
            throw;
        }
    }

    private static long ElapsedMs(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}