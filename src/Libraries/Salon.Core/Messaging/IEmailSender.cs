namespace Salon.Core.Messaging;

/// <summary>Email transport. Phase 1 delivers to Mailpit over plain SMTP; M3 adds Postmark/SES behind the same contract.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string? TextBody = null);