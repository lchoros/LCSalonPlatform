using FluentValidation;
using Salon.Core.Settings;
using Salon.Services.Localization;

namespace Salon.Web.Features.Settings.Notifications;

public sealed class SaveNotificationSettingsValidator : AbstractValidator<SaveNotificationSettingsCommand>
{
    public SaveNotificationSettingsValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.FirstReminderHours)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage(_ => localizer["Settings.Notifications.Validation.HoursRequired"])
            .InclusiveBetween(0, NotificationSettings.MaxLeadHours)
            .WithMessage(_ => localizer.Format("Settings.Notifications.Validation.HoursRange", NotificationSettings.MaxLeadHours));

        RuleFor(c => c.SecondReminderHours)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage(_ => localizer["Settings.Notifications.Validation.HoursRequired"])
            .InclusiveBetween(0, NotificationSettings.MaxLeadHours)
            .WithMessage(_ => localizer.Format("Settings.Notifications.Validation.HoursRange", NotificationSettings.MaxLeadHours))
            // With both switched on, the second one is the one nearer the visit; equal times would be one message twice.
            .Must((c, second) => c.FirstReminderHours is not > 0 || second is not > 0 || second < c.FirstReminderHours)
            .WithMessage(_ => localizer["Settings.Notifications.Validation.SecondCloser"]);

        RuleFor(c => c.QuietFrom)
            .Must((c, _) => c.ParsedQuietFrom is not null)
            .WithMessage(_ => localizer["Settings.Notifications.Validation.TimeInvalid"]);

        RuleFor(c => c.QuietTo)
            .Must((c, _) => c.ParsedQuietTo is not null)
            .WithMessage(_ => localizer["Settings.Notifications.Validation.TimeInvalid"]);
    }
}