using Microsoft.Extensions.Logging;

using Salon.Core.Messaging;
using Salon.Core.Settings;
using Salon.Services.Messaging;
using Salon.Services.Settings;

namespace Salon.Services.Notifications;

/// <summary>
/// Hands one message about an appointment to one channel (ADR 0025): renders the template in the salon's culture and
/// gives it to the SMS or the email sender. Each caller decides <em>whether</em> a message is due; this only knows
/// <em>how</em> it goes out, so the rules are written once and both channels behave the same. Log lines carry ids,
/// never a phone number or an address.
/// </summary>
public sealed class AppointmentNotifier(
    IMessageTemplateService templates,
    ISettingService settings,
    ISmsSender sms,
    IEmailSender email,
    ILogger<AppointmentNotifier> logger)
{
    /// <summary>
    /// True when the message was handed to the channel's sender; false when the client cannot be reached on that channel
    /// (no phone, no email) or the salon has switched emails off. A sender that throws is the caller's to handle.
    /// </summary>
    public async Task<bool> SendAsync(MessageChannel channel, string templateKey, AppointmentMessageData data, MessageTokens tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(templateKey);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(tokens);

        var to = channel == MessageChannel.Email ? data.ClientEmail : data.ClientPhone;
        if (to is null)
        {
            logger.LogInformation("The client of appointment {AppointmentId} cannot be reached by {Channel}; {Template} skipped", data.AppointmentId, channel, templateKey);
            return false;
        }

        if (channel == MessageChannel.Email && !(await settings.LoadAsync<NotificationSettings>(cancellationToken).ConfigureAwait(false)).EmailEnabled)
        {
            logger.LogDebug("Emails are switched off for tenant {TenantId}; {Template} for appointment {AppointmentId} skipped", data.Tenant.Id, templateKey, data.AppointmentId);
            return false;
        }

        var rendered = await templates.RenderAsync(templateKey, channel, data.Tenant.Culture, tokens, cancellationToken).ConfigureAwait(false);
        if (!rendered.IsComplete)
        {
            logger.LogWarning("Template {Template} ({Channel}) of tenant {TenantId} has unresolved tokens: {Tokens}", templateKey, channel, data.Tenant.Id, string.Join(", ", rendered.MissingTokens));
        }

        if (channel == MessageChannel.Email)
        {
            await email.SendAsync(new EmailMessage(to, rendered.Subject ?? data.Tenant.Name, EmailBodies.ToHtml(rendered.Body), rendered.Body), cancellationToken).ConfigureAwait(false);
            logger.LogInformation("{Template} email for appointment {AppointmentId} handed to the mail sender", templateKey, data.AppointmentId);
        }
        else
        {
            await sms.SendAsync(new SmsMessage(to, rendered.Body), cancellationToken).ConfigureAwait(false);
            logger.LogInformation("{Template} SMS for appointment {AppointmentId} handed to {Provider}", templateKey, data.AppointmentId, sms.SystemName);
        }

        return true;
    }
}