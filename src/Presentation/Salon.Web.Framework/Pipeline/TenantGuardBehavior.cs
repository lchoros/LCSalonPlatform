using Salon.Core.Cqrs;
using Salon.Core.Tenancy;

namespace Salon.Web.Framework.Pipeline;

/// <summary>
/// Refuses any request that is not an <see cref="IPlatformRequest"/> when no tenant is in scope. Runs before the
/// transaction so a tenant-less command never touches the database. Validation runs first so the user sees input
/// errors even when the tenant is missing.
/// </summary>
public sealed class TenantGuardBehavior<TRequest, TResult>(ITenantContext tenantContext)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (request is not IPlatformRequest && tenantContext.TenantId is null)
        {
            throw new TenantViolationException(
                $"{typeof(TRequest).Name} requires a tenant in scope. Mark it IPlatformRequest only if it must run at platform level.");
        }

        return await next();
    }
}