using Microsoft.EntityFrameworkCore;
using Salon.Data;

namespace Salon.Web.Framework.Pipeline;

/// <summary>Trivial adapter over the scoped <see cref="SalonDbContext"/> transaction API.</summary>
internal sealed class EfUnitOfWork(SalonDbContext dbContext) : IUnitOfWork
{
    public bool HasActiveTransaction => dbContext.Database.CurrentTransaction is not null;

    public Task BeginTransactionAsync(CancellationToken cancellationToken)
        => dbContext.Database.BeginTransactionAsync(cancellationToken);

    public Task CommitAsync(CancellationToken cancellationToken)
        => dbContext.Database.CommitTransactionAsync(cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken)
        => dbContext.Database.RollbackTransactionAsync(cancellationToken);
}