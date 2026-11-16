using System.Linq.Expressions;
using FluentValidation;
using Salon.Core.Cqrs;
using Salon.Core.Settings;
using Salon.Services.Localization;
using Salon.Services.Settings;

namespace Salon.Web.Features.Settings.NextVisit;

/// <summary>The weeks offered for the next visit as the form shows them (ADR 0039).</summary>
public sealed record GetNextVisitQuery : IQuery<SaveNextVisitCommand>;

/// <summary>
/// The form as posted: up to three choices, each a whole number of weeks, 0 to leave one out.
/// <see cref="SaveNextVisitValidator"/> holds the rules.
/// </summary>
public sealed record SaveNextVisitCommand(int? FirstWeeks, int? SecondWeeks, int? ThirdWeeks) : ICommand<bool>
{
    public static SaveNextVisitCommand From(RebookSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new SaveNextVisitCommand(settings.FirstWeeks, settings.SecondWeeks, settings.ThirdWeeks);
    }

    /// <summary>
    /// What the desk would be offered with these values: the same rule the checkout uses, so the page cannot promise
    /// something else. A field that is empty or out of range is left out, as it would be.
    /// </summary>
    public IReadOnlyList<int> Offered() => RebookRules.Choices(new RebookSettings
    {
        FirstWeeks = FirstWeeks ?? 0,
        SecondWeeks = SecondWeeks ?? 0,
        ThirdWeeks = ThirdWeeks ?? 0,
    });
}

public sealed class SaveNextVisitValidator : AbstractValidator<SaveNextVisitCommand>
{
    public SaveNextVisitValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        foreach (var field in new Expression<Func<SaveNextVisitCommand, int?>>[]
        {
            c => c.FirstWeeks,
            c => c.SecondWeeks,
            c => c.ThirdWeeks,
        })
        {
            RuleFor(field)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .WithMessage(_ => localizer["Settings.NextVisit.Validation.Required"])
                .InclusiveBetween(0, RebookSettings.MaxWeeks)
                .WithMessage(_ => localizer.Format("Settings.NextVisit.Validation.Range", RebookSettings.MaxWeeks));
        }
    }
}

public sealed class GetNextVisitHandler(ISettingService settings) : IRequestHandler<GetNextVisitQuery, SaveNextVisitCommand>
{
    public async Task<SaveNextVisitCommand> HandleAsync(GetNextVisitQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SaveNextVisitCommand.From(await settings.LoadAsync<RebookSettings>(cancellationToken));
    }
}

public sealed class SaveNextVisitHandler(ISettingService settings) : IRequestHandler<SaveNextVisitCommand, bool>
{
    public async Task<bool> HandleAsync(SaveNextVisitCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has run in the pipeline; this only narrows the types.
        if (request.FirstWeeks is not { } first || request.SecondWeeks is not { } second || request.ThirdWeeks is not { } third)
        {
            return false;
        }

        // Load first: a property this form does not show must keep its value.
        var current = await settings.LoadAsync<RebookSettings>(cancellationToken);
        current.FirstWeeks = first;
        current.SecondWeeks = second;
        current.ThirdWeeks = third;
        await settings.SaveAsync(current, cancellationToken);
        return true;
    }
}