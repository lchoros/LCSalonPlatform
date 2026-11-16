using FluentValidation;
using Salon.Core.Cqrs;
using Salon.Core.Notifications;
using Salon.Core.Settings;
using Salon.Services.Localization;
using Salon.Services.Notifications;
using Salon.Services.Settings;

namespace Salon.Web.Features.Settings.DailySummary;

/// <summary>The daily summary's switch and time as the form shows them, with what today's email would say (ADR 0050).</summary>
public sealed record GetDailySummaryQuery : IQuery<DailySummaryPage>;

/// <param name="Preview">Today's summary as it would go out now, and to how many owners.</param>
public sealed record DailySummaryPage(SaveDailySummaryCommand Form, DailySummaryPreview Preview);

/// <summary>
/// The form as posted: the switch and a time of day such as "20:00" on the salon's clock.
/// <see cref="SaveDailySummaryValidator"/> holds the rule.
/// </summary>
public sealed record SaveDailySummaryCommand(bool Enabled, string? SendAt) : ICommand<bool>
{
    public static SaveDailySummaryCommand From(DailySummarySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new SaveDailySummaryCommand(settings.Enabled, DailySummaryRules.ToClock(settings.SendAtMinute));
    }
}

public sealed class SaveDailySummaryValidator : AbstractValidator<SaveDailySummaryCommand>
{
    public SaveDailySummaryValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.SendAt)
            .Must(time => DailySummaryRules.ParseClock(time) is not null)
            .WithMessage(_ => localizer["Settings.DailySummary.Validation.Time"]);
    }
}

public sealed class GetDailySummaryHandler(ISettingService settings, IDailySummaryService summaries) : IRequestHandler<GetDailySummaryQuery, DailySummaryPage>
{
    public async Task<DailySummaryPage> HandleAsync(GetDailySummaryQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new DailySummaryPage(
            SaveDailySummaryCommand.From(await settings.LoadAsync<DailySummarySettings>(cancellationToken)),
            await summaries.PreviewAsync(cancellationToken));
    }
}

public sealed class SaveDailySummaryHandler(ISettingService settings) : IRequestHandler<SaveDailySummaryCommand, bool>
{
    public async Task<bool> HandleAsync(SaveDailySummaryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has run in the pipeline; this only narrows the type.
        if (DailySummaryRules.ParseClock(request.SendAt) is not { } minute)
        {
            return false;
        }

        // Load first: a property this form does not show must keep its value.
        var current = await settings.LoadAsync<DailySummarySettings>(cancellationToken);
        current.Enabled = request.Enabled;
        current.SendAtMinute = minute;
        await settings.SaveAsync(current, cancellationToken);
        return true;
    }
}