using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
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

    /// <summary>The messages recorded for one field, in order; empty when the field is fine (ADR 0043).</summary>
    public static IReadOnlyList<string> ErrorsFor(this ModelStateDictionary modelState, string key)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        return modelState.TryGetValue(key, out var entry) ? entry.Errors.Select(e => e.ErrorMessage).ToList() : [];
    }

    /// <summary>The class MVC's own tag helpers put on a field with an error, for a field written by hand.</summary>
    public static string InvalidClass(this ModelStateDictionary modelState, string key)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        return modelState[key]?.Errors.Count > 0 ? "input-validation-error" : string.Empty;
    }
}

/// <summary>Where a request sits in the admin menu (ADR 0033).</summary>
public static class AdminMenu
{
    /// <summary>
    /// The value of <c>aria-current</c> for a menu link: "true" when the request's address is the area's or lies under
    /// it, null otherwise. "true", not "page": the link names the area, which is often not the page itself.
    /// </summary>
    public static string? Current(this HttpRequest request, string area)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(area);
        var path = request.Path.Value ?? string.Empty;
        return path.Equals(area, StringComparison.OrdinalIgnoreCase) || path.StartsWith(area + "/", StringComparison.OrdinalIgnoreCase) ? "true" : null;
    }
}