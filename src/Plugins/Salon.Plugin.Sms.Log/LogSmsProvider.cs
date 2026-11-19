using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Salon.Core.Messaging;

namespace Salon.Plugin.Sms.Log;

/// <summary>
/// Phase 1 SMS provider. Writes the message to the log and mirrors it as an email — to Mailpit locally, or to the
/// configured <see cref="SmsMirrorOptions.MirrorAddress"/> on a hosted pilot — so the whole flow can be inspected
/// in an inbox. Sends nothing to a real phone.
/// </summary>
public sealed class LogSmsProvider(IEmailSender emailSender, IOptions<SmsMirrorOptions> options, ILogger<LogSmsProvider> logger) : ISmsSender
{
    public const string PluginSystemName = "Sms.Log";

    /// <summary>Mailpit accepts any recipient; this mailbox groups SMS mirrors together.</summary>
    public const string MirrorMailbox = "sms@mailpit.local";

    public string SystemName => PluginSystemName;

    public string DisplayNameKey => "Plugins.Sms.Log.DisplayName";

    public async Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        logger.LogInformation("SMS to {To}: {Body}", message.ToE164, message.Body);

        var subject = $"SMS to {message.ToE164}";
        var html = $"<p><strong>To:</strong> {System.Net.WebUtility.HtmlEncode(message.ToE164)}</p><pre>{System.Net.WebUtility.HtmlEncode(message.Body)}</pre>";
        await emailSender.SendAsync(new EmailMessage(options.Value.MirrorAddress, subject, html, message.Body), cancellationToken);
    }
}