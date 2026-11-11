using FluentValidation;
using FluentValidation.Results;

using Salon.Core.Cqrs;

namespace Salon.Web.Framework.Pipeline;

/// <summary>
/// Runs every FluentValidation <see cref="IValidator{T}"/> registered for the request. If any rule fails the handler
/// is not called and a <see cref="ValidationException"/> carrying all failures is thrown.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResult>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var failures = new List<ValidationFailure>();

        foreach (var validator in validators)
        {
            // A fresh context per validator: FluentValidation accumulates failures on a shared context.
            var result = await validator.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}