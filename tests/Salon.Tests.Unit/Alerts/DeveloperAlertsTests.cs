using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NodaTime;
using Salon.Core.Messaging;
using Salon.Web.Framework.Alerts;

namespace Salon.Tests.Unit.Alerts;

/// <summary>
/// The developer's alerts (ADR 0093): nothing without an address; one message per fault per quiet period, the next
/// one saying how often it happened meanwhile; different faults do not silence each other; and a mail server that is
/// down never throws into whoever reported.
/// </summary>
public class DeveloperAlertsTests
{
    private static readonly Instant Start = Instant.FromUtc(2026, 11, 21, 9, 0);

    [Fact]
    public async Task Without_an_address_nothing_is_sent()
    {
        var (alerts, mail, _) = Build(email: null);

        await alerts.RaiseAsync("A page failed: X", "detail");

        mail.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task One_fault_is_one_message_per_quiet_period_and_the_next_counts_what_was_held_back()
    {
        var (alerts, mail, clock) = Build("dev@example.test");

        await alerts.RaiseAsync("A page failed: X", "first");
        await alerts.RaiseAsync("A page failed: X", "second");
        clock.Now = Start + Duration.FromMinutes(59);
        await alerts.RaiseAsync("A page failed: X", "third");

        var only = mail.Sent.ShouldHaveSingleItem();
        only.To.ShouldBe("dev@example.test");
        only.Subject.ShouldBe("[Salon.Tests Testing] A page failed: X");
        only.TextBody.ShouldNotBeNull().ShouldContain("first");

        clock.Now = Start + Duration.FromMinutes(61);
        await alerts.RaiseAsync("A page failed: X", "fourth");

        mail.Sent.Count.ShouldBe(2);
        mail.Sent[1].TextBody.ShouldNotBeNull().ShouldContain("2 more time(s)");
    }

    [Fact]
    public async Task Different_faults_do_not_silence_each_other()
    {
        var (alerts, mail, _) = Build("dev@example.test");

        await alerts.RaiseAsync("A page failed: X", "a");
        await alerts.RaiseAsync("A background job failed: Reminders.Run", "b");

        mail.Sent.Select(m => m.Subject).ShouldBe(["[Salon.Tests Testing] A page failed: X", "[Salon.Tests Testing] A background job failed: Reminders.Run"]);
    }

    [Fact]
    public async Task A_mail_server_that_is_down_does_not_throw_into_whoever_reported()
    {
        var (alerts, mail, _) = Build("dev@example.test");
        mail.Fail = true;

        await Should.NotThrowAsync(() => alerts.RaiseAsync("A page failed: X", "detail"));
    }

    private static (DeveloperAlerts Alerts, FakeMail Mail, MovableClock Clock) Build(string? email)
    {
        var mail = new FakeMail();
        var clock = new MovableClock { Now = Start };
        var services = new ServiceCollection().AddSingleton<IEmailSender>(mail).BuildServiceProvider();
        var alerts = new DeveloperAlerts(
            new FixedOptions(new AlertOptions { Email = email }),
            services.GetRequiredService<IServiceScopeFactory>(),
            new TestEnvironment(),
            clock,
            NullLogger<DeveloperAlerts>.Instance);
        return (alerts, mail, clock);
    }

    private sealed class FakeMail : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public bool Fail { get; set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new InvalidOperationException("The mail server is down.");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class MovableClock : IClock
    {
        public Instant Now { get; set; }

        public Instant GetCurrentInstant() => Now;
    }

    private sealed class FixedOptions(AlertOptions value) : IOptionsMonitor<AlertOptions>
    {
        public AlertOptions CurrentValue => value;

        public AlertOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AlertOptions, string?> listener) => null;
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "Salon.Tests";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}