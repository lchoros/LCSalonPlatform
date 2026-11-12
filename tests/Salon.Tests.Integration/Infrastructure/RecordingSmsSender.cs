using System.Collections.Concurrent;
using System.Text.RegularExpressions;

using Salon.Core.Messaging;

namespace Salon.Tests.Integration.Infrastructure;

/// <summary>
/// Replaces the Log SMS provider in the test host so a test can read the verification code and the confirmation text
/// the way a client reads their phone. Keeps every message in memory; nothing is logged or mailed.
/// </summary>
public sealed partial class RecordingSmsSender : ISmsSender
{
    private readonly ConcurrentQueue<SmsMessage> _messages = new();

    public string SystemName => "Sms.Recording";

    public string DisplayNameKey => "Plugins.Sms.Recording.DisplayName";

    public IReadOnlyList<SmsMessage> Messages => [.. _messages];

    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public void Clear() => _messages.Clear();

    /// <summary>The six digits in the most recent text to <paramref name="phoneE164"/>, or null.</summary>
    public string? LastCodeFor(string phoneE164)
    {
        var last = _messages.LastOrDefault(m => m.ToE164 == phoneE164);
        if (last is null)
        {
            return null;
        }

        var match = CodePattern().Match(last.Body);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(@"(?<!\d)\d{6}(?!\d)")]
    private static partial Regex CodePattern();
}
