using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Salon.Web.Framework.Mvc;

public static class ModelStateExtensions
{
    /// <summary>
    /// Copies FluentValidation failures (as thrown by the pipeline's <c>ValidationBehavior</c>) into the model state,
    /// keyed by property name, so the form re-renders with each message next to its field.
    /// </summary>
    public static void AddValidationFailures(this ModelStateDictionary modelState, IEnumerable<ValidationFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        ArgumentNullException.ThrowIfNull(failures);

        foreach (var failure in failures)
        {
            modelState.AddModelError(failure.PropertyName ?? string.Empty, failure.ErrorMessage);
        }
    }
}