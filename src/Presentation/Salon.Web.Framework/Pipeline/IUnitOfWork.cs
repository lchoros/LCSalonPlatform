namespace Salon.Web.Framework.Pipeline;

/// <summary>
/// The transaction boundary used by <see cref="TransactionBehavior{TRequest, TResult}"/>. The default implementation
/// wraps <c>SalonDbContext.Database</c>; tests substitute it. Slices do not use this directly.
/// </summary>
public interface IUnitOfWork
{
    bool HasActiveTransaction { get; }

    Task BeginTransactionAsync(CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}