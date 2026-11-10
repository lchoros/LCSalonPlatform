using Salon.Core.Plugins;

namespace Salon.Core.Messaging;

/// <summary>SMS plugin contract. Phase 1: <c>LogSmsProvider</c>. M3: Twilio (UK), SMSAPI.pl (PL).</summary>
public interface ISmsSender : IPlugin
{
    Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
}

/// <param name="ToE164">Recipient in E.164 form, e.g. +447700900123 or +48600700800.</param>
/// <param name="Body">Plain text; keep under 160 GSM characters where possible.</param>
public sealed record SmsMessage(string ToE164, string Body);