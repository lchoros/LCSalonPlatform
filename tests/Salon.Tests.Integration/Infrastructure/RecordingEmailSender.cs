using System.Collections.Concurrent;
using Salon.Core.Messaging;

namespace Salon.Tests.Integration.Infrastructure;

/// <summary>
/// Stands in for the SMTP sender in the test host: keeps every message in memory so a test can read what a client
/// would have received, and can be told to fail so a test can see what an unreachable mail server does.
/// </summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyList<EmailMessage> Messages => [.. _messages];

    /// <summary>While true every send throws, like a mail server that is down.</summary>
    public bool Fail { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (Fail)
        {
            throw new InvalidOperationException("The test mail server is down.");
        }

        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public void Clear()
    {
        _messages.Clear();
        Fail = false;
    }
}