using Salon.Core.Cqrs;
using Salon.Core.Settings;
using Salon.Services.Settings;

namespace Salon.Web.Features.Settings.Notifications;

public sealed class GetNotificationSettingsHandler(ISettingService settings) : IRequestHandler<GetNotificationSettingsQuery, SaveNotificationSettingsCommand>
{
    public async Task<SaveNotificationSettingsCommand> HandleAsync(GetNotificationSettingsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SaveNotificationSettingsCommand.From(await settings.LoadAsync<NotificationSettings>(cancellationToken));
    }
}

public sealed class SaveNotificationSettingsHandler(ISettingService settings) : IRequestHandler<SaveNotificationSettingsCommand, bool>
{
    public async Task<bool> HandleAsync(SaveNotificationSettingsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has run in the pipeline; these only narrow the types.
        if (request.FirstReminderHours is not { } first || request.SecondReminderHours is not { } second
            || request.ParsedQuietFrom is not { } quietFrom || request.ParsedQuietTo is not { } quietTo)
        {
            return false;
        }

        // Load first: a property this form does not show must keep its value.
        var current = await settings.LoadAsync<NotificationSettings>(cancellationToken);
        current.RemindersEnabled = request.RemindersEnabled;
        current.FirstReminderHours = first;
        current.SecondReminderHours = second;
        current.QuietHoursStartMinute = (quietFrom.Hour * 60) + quietFrom.Minute;
        current.QuietHoursEndMinute = (quietTo.Hour * 60) + quietTo.Minute;
        await settings.SaveAsync(current, cancellationToken);
        return true;
    }
}