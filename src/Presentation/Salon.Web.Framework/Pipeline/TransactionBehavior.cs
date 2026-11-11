using Salon.Core.Cqrs;

namespace Salon.Web.Framework.Pipeline;

/// <summary>
/// Wraps every <see cref="ICommand{TResult}"/> in one database transaction: begin, run the handler, commit; roll back
/// on any exception. Joins a transaction that is already open (nested commands, tests) and leaves queries alone.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResult>(IUnitOfWork unitOfWork)
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
            // Do not let a cancelled token stop the rollback.
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}