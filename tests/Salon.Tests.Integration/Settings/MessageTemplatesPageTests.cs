using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Identity;
using Salon.Core.Messaging;
using Salon.Core.Tenancy;
using Salon.Services.Messaging;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Settings;

/// <summary>
/// The messages page (ADR 0030): the owner or a manager reads every message clients receive, writes the salon's own
/// wording, previews it with made-up details and the number of texts it costs, and goes back to the standard wording.
/// What would break a message is refused next to its field. One salon's wording never reaches another.
/// </summary>
[Collection(SharedDatabase.Name)]
public class MessageTemplatesPageTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string ListPath = "/admin/settings/messages";
    private const string ConfirmationSms = ListPath + "/Booking.Confirmation/sms";
    private const string ConfirmationEmail = ListPath + "/Booking.Confirmation/email";
    private const string CodeSms = ListPath + "/Booking.Code/sms";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    private Tenant _studio = null!;
    private Tenant _polish = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_list_names_every_message_per_channel_with_the_standard_wording_at_first()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetAsync(client, ListPath);

        html.ShouldContain("Messages to clients");
        html.ShouldContain("href=\"/admin/settings/messages\" data-testid=\"settings-nav-messages\" aria-current=\"page\"");
        foreach (var name in new[] { "Booking confirmation", "Reminder", "Visit cancelled", "New time for a visit", "Verification code" })
        {
            html.ShouldContain($">{name}</h2>");
        }

        html.ShouldContain($"href=\"{ConfirmationSms}\" data-testid=\"edit-Booking.Confirmation-Sms\"");
        html.ShouldContain($"href=\"{ConfirmationEmail}\" data-testid=\"edit-Booking.Confirmation-Email\"");
        html.ShouldContain($"href=\"{CodeSms}\" data-testid=\"edit-Booking.Code-Sms\"");
        html.ShouldNotContain("data-testid=\"edit-Booking.Code-Email\"", Case.Sensitive, "the code goes out by text only");
        Count(html, ">Standard wording<").ShouldBe(9, "four messages on two channels and the code on one");
        html.ShouldNotContain(">Your own wording<");
    }

    [Fact]
    public async Task The_edit_page_shows_the_wording_what_can_go_in_and_an_example_with_its_cost()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var sms = await GetAsync(client, ConfirmationSms);

        sms.ShouldContain("data-testid=\"status\">Standard wording<");
        sms.ShouldContain(">Hi {{ClientFirstName}}, your {{ServiceName}} at {{SalonName}} is booked for {{AppointmentStart}}. Need to cancel? {{CancelUrl}}</textarea>");
        sms.ShouldNotContain("data-testid=\"subject\"", Case.Sensitive, "a text has no subject");
        foreach (var token in new[] { "SalonName", "ClientFirstName", "ServiceName", "StaffName", "AppointmentStart", "CancelUrl" })
        {
            sms.ShouldContain("{{" + token + "}}</code>");
        }

        sms.ShouldContain("the link the client uses to change or cancel the visit");
        sms.ShouldContain("data-testid=\"preview-body\">Hi Olivia, your Cut and blow-dry at Studio 20 is booked for ");
        sms.ShouldContain("/v/exampleexampleexamplee<");
        // How many texts depends on the length of the salon's address, so only the line itself is checked here.
        sms.ShouldContain("data-testid=\"preview-length\" data-segments=\"");
        sms.ShouldContain("Texts needed: ");
        sms.ShouldNotContain("data-testid=\"preview-unsaved\"");
        sms.ShouldNotContain("data-testid=\"preview-unicode\"");
        sms.ShouldNotContain("data-testid=\"reset-form\"", Case.Sensitive, "there is nothing to go back from");

        var email = await GetAsync(client, ConfirmationEmail);
        email.ShouldContain("value=\"Your {{ServiceName}} at {{SalonName}} is booked\"");
        email.ShouldContain("data-testid=\"preview-subject\">Your Cut and blow-dry at Studio 20 is booked<");
        email.ShouldNotContain("data-testid=\"preview-length\"", Case.Sensitive, "an email is not counted in texts");

        var code = await GetAsync(client, CodeSms);
        code.ShouldContain("the code the client types in<span class=\"font-medium\">, must stay in the message</span>");
        code.ShouldContain("data-testid=\"preview-body\">Studio 20: your code is 111111. It expires in 10 minutes.<");
    }

    [Fact]
    public async Task The_salons_own_wording_is_saved_used_and_kept_to_that_salon()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        const string own = "Hello {{ClientFirstName}}! {{SalonName}} has you down for {{AppointmentStart}}. Change of plan? {{CancelUrl}}";

        var response = await PostAsync(client, ConfirmationSms, new() { ["body"] = "  " + own + "\r\n" });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe(ConfirmationSms);
        var after = await GetAsync(client, ConfirmationSms);
        after.ShouldContain("The message has been saved.");
        after.ShouldContain("data-testid=\"status\">Your own wording<");
        after.ShouldContain($">{own}</textarea>");
        after.ShouldContain("data-testid=\"preview-body\">Hello Olivia! Studio 20 has you down for ");
        after.ShouldContain("data-testid=\"standard-body\">Hi {{ClientFirstName}}, your {{ServiceName}} at {{SalonName}} is booked for");
        after.ShouldContain("data-testid=\"reset-form\"");

        var saved = await TemplatesAsync(_studio.Id, s => s.GetAsync(MessageTemplateCatalog.BookingConfirmation, MessageChannel.Sms, "en-GB"));
        saved.ShouldBe(new MessageTemplateText(null, own, IsTenantOverride: true));

        // Only that one row of the list changed, and the other salon still sends the standard text.
        var list = await GetAsync(client, ListPath);
        list.ShouldContain("data-testid=\"status-Booking.Confirmation-Sms\">Your own wording<");
        Count(list, ">Your own wording<").ShouldBe(1);
        (await TemplatesAsync(_polish.Id, s => s.GetAsync(MessageTemplateCatalog.BookingConfirmation, MessageChannel.Sms, "pl-PL"))).IsTenantOverride.ShouldBeFalse();
    }

    [Fact]
    public async Task An_email_keeps_its_subject_and_its_line_breaks()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);

        var response = await PostAsync(client, ConfirmationEmail, new()
        {
            ["subject"] = "  See you at {{SalonName}}  ",
            ["body"] = "Dear {{ClientFirstName}},\r\n\r\nyour {{ServiceName}} is on {{AppointmentStart}}.\r\n{{CancelUrl}}",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var saved = await TemplatesAsync(_studio.Id, s => s.GetAsync(MessageTemplateCatalog.BookingConfirmation, MessageChannel.Email, "en-GB"));
        saved.Subject.ShouldBe("See you at {{SalonName}}");
        saved.Body.ShouldBe("Dear {{ClientFirstName}},\n\nyour {{ServiceName}} is on {{AppointmentStart}}.\n{{CancelUrl}}");
        (await GetAsync(client, ConfirmationEmail)).ShouldContain("data-testid=\"preview-subject\">See you at Studio 20<");
    }

    [Theory]
    [InlineData(ConfirmationSms, "", "", "body-error", "Write the message.")]
    [InlineData(ConfirmationSms, "", "   ", "body-error", "Write the message.")]
    [InlineData(ConfirmationSms, "", "Your {{ServiceName}} costs {{Price}}.", "body-error", "This message cannot use {{Price}}. It can use: {{SalonName}}, {{ClientFirstName}}, {{ServiceName}}, {{StaffName}}, {{AppointmentStart}}, {{CancelUrl}}.")]
    [InlineData(CodeSms, "", "Welcome to {{SalonName}}!", "body-error", "Keep {{Code}} in the message. Without it the message is of no use to the client.")]
    [InlineData(ConfirmationEmail, "", "See you soon.", "subject-error", "Write the subject.")]
    [InlineData(ConfirmationEmail, "Code {{Code}}", "See you soon.", "subject-error", "This message cannot use {{Code}}.")]
    public async Task Wording_that_would_break_a_message_is_refused_next_to_its_field(string path, string subject, string body, string errorId, string message)
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await PostAsync(client, path, new() { ["subject"] = subject, ["body"] = body });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.ShouldContain($"data-testid=\"{errorId}\">{message}");
        html.ShouldContain("data-testid=\"preview-unsaved\"", Case.Sensitive, "what was typed is shown, clearly as not saved");
        html.ShouldContain("data-testid=\"status\">Standard wording<");
        (await TemplatesAsync(_studio.Id, s => s.GetAsync(MessageTemplateCatalog.BookingConfirmation, MessageChannel.Sms, "en-GB"))).IsTenantOverride.ShouldBeFalse();
        (await TemplatesAsync(_studio.Id, s => s.GetAsync(MessageTemplateCatalog.BookingCode, MessageChannel.Sms, "en-GB"))).IsTenantOverride.ShouldBeFalse();
    }

    [Fact]
    public async Task A_text_that_is_too_long_is_refused_by_the_page_and_by_the_service()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var tooLong = new string('a', MessageTemplateRules.SmsBodyMaxLength + 1);

        var response = await PostAsync(client, ConfirmationSms, new() { ["body"] = tooLong });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).ShouldContain("data-testid=\"body-error\">Use at most 480 characters.");
        await Should.ThrowAsync<ArgumentException>(() => TemplatesAsync(_studio.Id, async s =>
        {
            await s.SaveOverrideAsync(MessageTemplateCatalog.BookingConfirmation, MessageChannel.Sms, "en-GB", null, tooLong);
            return true;
        }));
        await Should.ThrowAsync<ArgumentException>(() => TemplatesAsync(_studio.Id, async s =>
        {
            await s.SaveOverrideAsync(MessageTemplateCatalog.BookingCode, MessageChannel.Sms, "en-GB", null, "No code here");
            return true;
        }));
    }

    [Fact]
    public async Task A_preview_shows_what_was_typed_and_what_it_costs_without_saving()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var polish = "Dzień dobry {{ClientFirstName}}, czekamy w środę. " + new string('x', 60);

        var response = await PostAsync(client, ConfirmationSms + "/preview", new() { ["body"] = polish });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.ShouldContain("This is a preview of what you typed. It is not saved yet.");
        html.ShouldContain("data-testid=\"preview-body\">Dzień dobry Olivia, czekamy w środę. ");
        html.ShouldContain("data-testid=\"preview-length\" data-segments=\"2\">Texts needed: 2.");
        html.ShouldContain("data-testid=\"preview-unicode\"");
        html.ShouldContain($">{polish}</textarea>");
        (await TemplatesAsync(_studio.Id, s => s.GetAsync(MessageTemplateCatalog.BookingConfirmation, MessageChannel.Sms, "en-GB"))).IsTenantOverride.ShouldBeFalse();
    }

    [Fact]
    public async Task Going_back_to_the_standard_wording_deletes_the_salons_own()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        (await PostAsync(client, ConfirmationSms, new() { ["body"] = "See you, {{ClientFirstName}}." })).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var response = await PostAsync(client, ConfirmationSms + "/reset", []);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var after = await GetAsync(client, ConfirmationSms);
        after.ShouldContain("The standard wording is in use again.");
        after.ShouldContain("data-testid=\"status\">Standard wording<");
        after.ShouldNotContain("data-testid=\"reset-form\"");
        (await TemplatesAsync(_studio.Id, s => s.GetAsync(MessageTemplateCatalog.BookingConfirmation, MessageChannel.Sms, "en-GB"))).IsTenantOverride.ShouldBeFalse();
    }

    [Fact]
    public async Task A_polish_salon_edits_its_polish_wording()
    {
        using var client = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var list = await GetAsync(client, ListPath);
        list.ShouldContain("Wiadomości do klientów");
        list.ShouldContain(">Potwierdzenie rezerwacji</h2>");
        var edit = await GetAsync(client, ConfirmationSms);
        edit.ShouldContain("Co dostaje klient");
        edit.ShouldContain("Anna");
        edit.ShouldContain("Strzyżenie z modelowaniem");

        (await PostAsync(client, ConfirmationSms, new() { ["body"] = "Do zobaczenia, {{ClientFirstName}}!" })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await TemplatesAsync(_polish.Id, s => s.GetAsync(MessageTemplateCatalog.BookingConfirmation, MessageChannel.Sms, "pl-PL"))).Body.ShouldBe("Do zobaczenia, {{ClientFirstName}}!");
        (await TemplatesAsync(_studio.Id, s => s.GetAsync(MessageTemplateCatalog.BookingConfirmation, MessageChannel.Sms, "en-GB"))).IsTenantOverride.ShouldBeFalse();
    }

    [Fact]
    public async Task A_manager_reaches_the_messages_and_nothing_else_under_settings()
    {
        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);

        var list = await GetAsync(manager, ListPath);

        list.ShouldContain("href=\"/admin/settings/messages\" data-testid=\"nav-settings\"");
        list.ShouldContain("data-testid=\"settings-nav-messages\"");
        list.ShouldNotContain("data-testid=\"settings-nav-notifications\"");
        list.ShouldNotContain("data-testid=\"settings-nav-salon-details\"");
        (await manager.GetAsync(new Uri("/admin/settings/notifications", UriKind.Relative))).Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        // The owner's own menu still leads to the first settings page.
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        (await GetAsync(owner, ListPath)).ShouldContain("href=\"/admin/settings/notifications\" data-testid=\"nav-settings\"");
    }

    [Fact]
    public async Task The_desk_an_unknown_message_and_a_post_without_a_token_get_nowhere()
    {
        foreach (var role in new[] { Role.Receptionist, Role.Staff })
        {
            using var denied = await factory.SignedInClientAsync(HostA, _studio.Id, role);
            (await denied.GetAsync(new Uri(ListPath, UriKind.Relative))).Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
            (await denied.GetAsync(new Uri(ConfirmationSms, UriKind.Relative))).Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        }

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        foreach (var path in new[] { ListPath + "/Booking.Nothing/sms", ListPath + "/Booking.Code/email", ListPath + "/Booking.Confirmation/fax" })
        {
            (await owner.GetAsync(new Uri(path, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
            (await PostAsync(owner, path, new() { ["body"] = "x" })).StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        }

        foreach (var path in new[] { ConfirmationSms, ConfirmationSms + "/preview", ConfirmationSms + "/reset" })
        {
            var withoutToken = await owner.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string> { ["body"] = "See you." }));
            withoutToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest, path);
        }

        (await TemplatesAsync(_studio.Id, s => s.GetAsync(MessageTemplateCatalog.BookingConfirmation, MessageChannel.Sms, "en-GB"))).IsTenantOverride.ShouldBeFalse();
    }

    private static int Count(string html, string text)
    {
        var count = 0;
        for (var at = html.IndexOf(text, StringComparison.Ordinal); at >= 0; at = html.IndexOf(text, at + text.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private async Task<T> TemplatesAsync<T>(Guid tenantId, Func<IMessageTemplateService, Task<T>> act)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await act(scope.ServiceProvider.GetRequiredService<IMessageTemplateService>());
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Posts with the token of the list page, which every role that may post can open.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetAsync(client, ConfirmationSms));
        return await client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(fields));
    }
}