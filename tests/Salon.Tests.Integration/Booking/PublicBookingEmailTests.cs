using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Messaging;
using Salon.Core.Settings;
using Salon.Data;
using Salon.Services.Settings;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Booking;

/// <summary>
/// The email channel (ADR 0025): a client who gave an address gets the confirmation and the messages for a cancelled or
/// moved visit by email as well as by text, the salon can switch emails off, and a mail server that is down delays the
/// email without ever repeating the text. The test host records emails instead of sending them.
/// </summary>
public partial class PublicBookingTests
{
    private const string ClientEmail = "olivia@example.com";

    [Fact]
    public async Task The_confirmation_goes_out_by_text_and_by_email_with_the_cancel_link()
    {
        using var client = factory.ClientFor(HostA);
        var (appointment, _) = await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        await DispatchAsync();

        ConfirmationTexts().ShouldHaveSingleItem();
        var email = factory.Emails.Messages.ShouldHaveSingleItem();
        email.To.ShouldBe(ClientEmail);
        email.Subject.ShouldBe("Your Cut at Studio 20 is booked");
        var text = email.TextBody.ShouldNotBeNull();
        text.ShouldStartWith("Hi Olivia,\nyour Cut with Anna at Studio 20 is booked for ");
        text.ShouldContain($"/book/cancel/{appointment.PublicToken}");
        email.HtmlBody.ShouldStartWith("<p>Hi Olivia,<br>");
        email.HtmlBody.ShouldContain($"/book/cancel/{appointment.PublicToken}\">", Case.Sensitive, "the link is clickable");
        email.HtmlBody.ShouldEndWith("</p>");
    }

    [Fact]
    public async Task With_emails_switched_off_only_the_text_goes_out()
    {
        await SaveNotificationSettingsAsync(s => s.EmailEnabled = false);
        using var client = factory.ClientFor(HostA);
        await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        await DispatchAsync();

        ConfirmationTexts().ShouldHaveSingleItem();
        factory.Emails.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_client_who_gave_no_email_gets_the_text_alone()
    {
        using var client = factory.ClientFor(HostA);
        var fields = Fields(At(10, 0), _annaId);
        fields.Remove("Email");
        var verifyPath = await StartAsync(client, fields);
        (await PostCodeAsync(client, verifyPath, factory.Sms.LastCodeFor(PhoneE164)!)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        await DispatchAsync();

        ConfirmationTexts().ShouldHaveSingleItem();
        factory.Emails.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_mail_server_that_is_down_delays_the_email_and_never_repeats_the_text()
    {
        using var client = factory.ClientFor(HostA);
        await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        factory.Emails.Fail = true;

        await DispatchAsync();

        // The text went, the email did not, and the event waits for another attempt.
        ConfirmationTexts().ShouldHaveSingleItem();
        factory.Emails.Messages.ShouldBeEmpty();
        (await PendingOutboxCountAsync()).ShouldBe(1);

        // The mail server is back: the retry sends the email and leaves the text alone.
        factory.Emails.Fail = false;
        await MakeOutboxDueAsync();
        await DispatchAsync();

        factory.Emails.Messages.ShouldHaveSingleItem().To.ShouldBe(ClientEmail);
        ConfirmationTexts().ShouldHaveSingleItem("the client is not texted a second time because the email was late");
        (await PendingOutboxCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_cancelled_visit_and_a_moved_visit_are_emailed_too()
    {
        using var client = factory.ClientFor(HostA);
        var (appointment, _) = await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        var changePath = $"/book/change/{appointment.PublicToken}";
        await DispatchAsync();
        factory.Emails.Clear();

        var page = await GetAsync(client, $"{changePath}?date={Iso(_day)}");
        await PostChangeAsync(client, changePath, At(11, 0), _annaId, page);
        await DispatchAsync();

        var moved = factory.Emails.Messages.ShouldHaveSingleItem();
        moved.Subject.ShouldBe("Your booking at Studio 20 has a new time");
        moved.TextBody.ShouldNotBeNull().ShouldContain("11:00");
        moved.TextBody.ShouldContain($"/book/cancel/{appointment.PublicToken}");

        factory.Emails.Clear();
        await CancelBySalonAsync(appointment.Id);
        await DispatchAsync();

        var cancelled = factory.Emails.Messages.ShouldHaveSingleItem();
        cancelled.To.ShouldBe(ClientEmail);
        cancelled.Subject.ShouldBe("Your booking at Studio 20 has been cancelled");
        cancelled.TextBody.ShouldNotBeNull().ShouldContain("has been cancelled");
    }

    /// <summary>The confirmation texts among everything "sent": the verification code is a text too.</summary>
    private List<SmsMessage> ConfirmationTexts()
        => factory.Sms.Messages.Where(m => m.Body.StartsWith("Hi Olivia, your Cut at Studio 20 is booked", StringComparison.Ordinal)).ToList();

    private async Task SaveNotificationSettingsAsync(Action<NotificationSettings> change)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var settings = scope.ServiceProvider.GetRequiredService<ISettingService>();
        var current = await settings.LoadAsync<NotificationSettings>();
        change(current);
        await settings.SaveAsync(current);
    }

    private async Task<int> PendingOutboxCountAsync()
    {
        await using var scope = factory.CreateTenantScope(null);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().OutboxMessages.CountAsync(m => m.ProcessedAt == null);
    }

    /// <summary>Skips the retry delay, as time passing would.</summary>
    private async Task MakeOutboxDueAsync()
    {
        await using var scope = factory.CreateTenantScope(null);
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.NextAttemptAt, (NodaTime.Instant?)null));
    }
}