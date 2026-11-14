using Salon.Core.Messaging;
using Salon.Services.Notifications;

namespace Salon.Tests.Unit.Notifications;

/// <summary>
/// Log lines name the channel with a fixed word. CodeQL on <c>main</c> read the enum member <c>Email</c> in a log call
/// as an email address being logged; a word chosen by a switch carries nothing from the caller.
/// </summary>
public class ChannelNameTests
{
    [Theory]
    [InlineData(MessageChannel.Sms, "SMS")]
    [InlineData(MessageChannel.Email, "email")]
    public void Each_channel_has_a_fixed_word_for_log_lines(MessageChannel channel, string expected)
    {
        AppointmentNotifier.ChannelName(channel).ShouldBe(expected);
    }

    [Fact]
    public void Every_channel_has_a_word_and_an_unknown_one_says_so()
    {
        Enum.GetValues<MessageChannel>().Select(AppointmentNotifier.ChannelName).ShouldNotContain("unknown");
        AppointmentNotifier.ChannelName((MessageChannel)99).ShouldBe("unknown");
    }
}