using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using Salon.Core.Messaging;

namespace Salon.Web.Framework.Alerts;

/// <summary>
/// Where the platform tells its developer that something failed (section "Alerts", ADR 0093). The address lives in
/// the server's own configuration, never in the repository; without it nothing is sent.
/// </summary>
public sealed class AlertOptions
{
    public const string SectionName = "Alerts";

    /// <summary>The developer's address; empty switches the alerts off.</summary>
    public string? Email { get; set; }

    /// <summary>The same fault is reported once in this many minutes, however often it happens.</summary>
    public int RepeatAfterMinutes { get; set; } = 60;
}

/// <summary>
/// Tells the developer that something failed while the site is still running: an error a person saw, a background job
/// that gave up. (A site that is down cannot say so — that is the outside monitor's job, through /health.)
/// </summary>
public interface IDeveloperAlerts
{
    /// <param name="fault">What failed, in a few words; also the key the repeats are grouped by.</param>
    /// <param name="detail">Types, ids and numbers only — never a name, a number or a message a person wrote.</param>
    Task RaiseAsync(string fault, string detail, CancellationToken cancellationToken = default);
}

/// <summary>
/// One email per fault per <see cref="AlertOptions.RepeatAfterMinutes"/>, with how often it happened since the last
/// one. Sending never throws into the request or the job that reported: a mail server that is down must not turn one
/// failure into two.
/// </summary>
public sealed class DeveloperAlerts(
    IOptionsMonitor<AlertOptions> options,
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    IClock clock,
    ILogger<DeveloperAlerts> logger) : IDeveloperAlerts
{
    private readonly ConcurrentDictionary<string, Seen> _seen = new(StringComparer.Ordinal);

    public async Task RaiseAsync(string fault, string detail, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fault);
        ArgumentNullException.ThrowIfNull(detail);

        var settings = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(settings.Email))
        {
            return;
        }

        var now = clock.GetCurrentInstant();
        var quiet = Duration.FromMinutes(Math.Max(1, settings.RepeatAfterMinutes));
        var send = false;
        var since = 0;
        _seen.AddOrUpdate(
            fault,
            _ =>
            {
                send = true;
                return new Seen(now, 0);
            },
            (_, seen) =>
            {
                if (now - seen.LastSent < quiet)
                {
                    return seen with { Suppressed = seen.Suppressed + 1 };
                }

                send = true;
                since = seen.Suppressed;
                return new Seen(now, 0);
            });
        if (!send)
        {
            return;
        }

        var subject = $"[{environment.ApplicationName} {environment.EnvironmentName}] {fault}";
        var text = $"{fault}\n\n{detail}\n\nWhen: {now} (UTC)\nSince the last message about this: {since} more time(s).\nThe same fault is reported again in {quiet.TotalMinutes:0} minutes at the earliest.";
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IEmailSender>()
                .SendAsync(new EmailMessage(settings.Email, subject, $"<pre>{WebUtility.HtmlEncode(text)}</pre>", text), cancellationToken)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Whatever the mail server did, the failure being reported is the one that matters; this one is logged.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "A developer alert could not be sent");
        }
    }

    private sealed record Seen(Instant LastSent, int Suppressed);
}