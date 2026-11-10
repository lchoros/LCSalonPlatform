using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Salon.Core.Messaging;
using Salon.Plugin.Sms.Log;

namespace Salon.Tests.Unit.Plugins;

public class LogSmsProviderTests
{
    [Fact]
    public async Task Mirrors_the_sms_to_the_mailpit_inbox()
    {
        var email = Substitute.For<IEmailSender>();
        var provider = new LogSmsProvider(email, NullLogger<LogSmsProvider>.Instance);

        await provider.SendAsync(new SmsMessage("+447700900123", "Your appointment is tomorrow at 10:00"));

        await email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m =>
                m.To == LogSmsProvider.MirrorMailbox
                && m.Subject.Contains("+447700900123", StringComparison.Ordinal)
                && m.TextBody == "Your appointment is tomorrow at 10:00"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Html_encodes_the_body()
    {
        var email = Substitute.For<IEmailSender>();
        var provider = new LogSmsProvider(email, NullLogger<LogSmsProvider>.Instance);

        await provider.SendAsync(new SmsMessage("+48600700800", "<b>Hi</b>"));

        await email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.HtmlBody.Contains("&lt;b&gt;Hi&lt;/b&gt;", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }
}