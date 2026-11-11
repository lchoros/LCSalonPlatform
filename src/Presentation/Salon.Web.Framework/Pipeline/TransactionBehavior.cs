using Microsoft.Extensions.Logging;

using Salon.Core.Cqrs;

namespace Salon.Web.Framework.Pipeline;

/// <summary>
/// Wraps every <see cref="ICommand{TResult}"/> in one database transaction: begin, run the handler, commit; roll back
/// on any exception. Joins a transaction that is already open (nested commands, tests) and leaves queries alone.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResult>(IUnitOfWork unitOfWork, ILogger<TransactionBehavior<TRequest, TResult>> logger)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (request is not ICommand<TResult> || unitOfWork.HasActiveTransaction)
        {
            return await next();
        }

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await next();
            await unitOfWork.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            try
            {
                // Do not let a cancelled token stop the rollback.
                await unitOfWork.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                // The handler's exception is the one the caller must see; the rollback failure goes to the log.
                logger.LogError(rollbackException, "Rollback failed after {Request} threw", typeof(TRequest).Name);
            }

            throw;
        }
    }
}