using System.Net;
using System.Text.RegularExpressions;
using Salon.Core.Identity;
using Salon.Core.Messaging;
using Salon.Core.Tenancy;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Settings;

/// <summary>
/// The tags and the preview of the messages page (ADR 0098): each message offers exactly its own tags, named in plain
/// words in the page's language; the server writes the preview with the example values coloured and a tag the message
/// does not offer marked as unknown (the same tags saving refuses); putting back the standard text asks first.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class MessageTemplateTagsPageTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string ListPath = "/admin/settings/messages";
    private const string ConfirmationSms = ListPath + "/Booking.Confirmation/sms";
    private const string CancelledSms = ListPath + "/Booking.Cancelled/sms";
    private const string ConfirmationEmail = ListPath + "/Booking.Confirmation/email";
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
    public async Task Every_message_offers_exactly_its_own_tags_as_buttons_the_script_switches_on()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        foreach (var definition in MessageTemplateCatalog.All)
        {
            foreach (var channel in definition.Channels)
            {
                var path = $"{ListPath}/{definition.Key}/{(channel == MessageChannel.Email ? "email" : "sms")}";
                var html = await GetAsync(client, path);

                var offered = ChipPattern().Matches(html).Select(m => m.Groups["name"].Value).ToList();
                offered.ShouldBe(definition.TokenNames, $"{path} offers its own tags, in catalogue order");
                Count(html, "class=\"tag-required\"").ShouldBe(definition.RequiredTokenNames.Count, path);
                Count(html, "type=\"button\" class=\"tag-chip\"").ShouldBe(definition.TokenNames.Count, path);
                Count(html, " data-tag-target").ShouldBe(channel == MessageChannel.Email ? 2 : 1, "the subject and the message of an email both take tags");
            }
        }

        var held = await GetAsync(client, ListPath + "/Waitlist.TimeHeld/sms");
        held.ShouldContain("data-tag=\"HeldUntil\"");
        held.ShouldContain("is kept for you until <mark class=\"tag-value\">", Case.Sensitive, "the time a waiting client is kept until has its own example");
        held.ShouldContain(", 08:00</mark>");
    }

    [Fact]
    public async Task Without_script_the_tags_are_a_list_of_each_name_and_how_to_type_it()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetAsync(client, ConfirmationSms);

        html.ShouldContain("data-testid=\"tag-ClientFirstName\" disabled>");
        html.ShouldContain("<span>Client's first name</span>");
        html.ShouldContain("<code class=\"tag-code\">{{ClientFirstName}}</code>");
        html.ShouldContain("data-when-noscript>Type a tag exactly as shown, with the double curly brackets.");
        html.ShouldContain("data-when-script hidden>Tap a tag to put it where the cursor is.<");
        html.ShouldContain("id=\"tags-explain\" class=\"field-hint\">The coloured words in the example are filled in for each client.<");
        html.ShouldContain("aria-describedby=\"tags-explain body-error\"");
        html.ShouldContain("data-testid=\"preview-message\" data-when-noscript>Preview<", Case.Sensitive, "the server's preview stays the way to one without script");
        html.ShouldContain("<script type=\"module\" src=\"/js/message-tags.js?v=");
        html.ShouldContain($"data-pattern=\"{MessageTemplateRenderer.PlaceholderPattern}\"", Case.Sensitive, "the script reads the server's own pattern");
        html.ShouldContain("data-unknown-label=\"unknown tag\"");
    }

    [Fact]
    public async Task The_tags_are_named_in_plain_words_in_the_language_of_the_page()
    {
        using var english = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var confirmation = await GetAsync(english, ConfirmationSms);
        foreach (var label in new[] { "Salon name", "Client's first name", "Service", "Staff member", "Date and time", "Link to change or cancel" })
        {
            confirmation.ShouldContain($"<span>{label}</span>");
        }

        (await GetAsync(english, CancelledSms)).ShouldContain("<span>Booking link</span>");
        (await GetAsync(english, ListPath + "/Waitlist.TimeHeld/sms")).ShouldContain("<span>Kept until</span>");
        confirmation.ShouldContain(">Tags you can add</h2>");

        using var polish = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        var polishConfirmation = await GetAsync(polish, ConfirmationSms);
        foreach (var label in new[] { "Nazwa salonu", "Imię klienta", "Usługa", "Pracownik", "Data i godzina", "Link do zmiany lub odwołania" })
        {
            polishConfirmation.ShouldContain($"<span>{label}</span>");
        }

        polishConfirmation.ShouldContain(">Znaczniki do wstawienia</h2>");
        polishConfirmation.ShouldContain("Kolorowe słowa w przykładzie są uzupełniane dla każdego klienta osobno.");
        polishConfirmation.ShouldContain("data-unknown-label=\"nieznany znacznik\"");
        (await GetAsync(polish, CancelledSms)).ShouldContain("<span>Link do rezerwacji</span>");
        (await GetAsync(polish, ListPath + "/Booking.Code/sms")).ShouldContain("<span>Kod</span>");
    }

    [Fact]
    public async Task An_unknown_or_misspelt_tag_is_marked_in_the_preview_and_refused_on_saving()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        // A misspelt name and one that belongs to another message: the cancellation has no link to change the visit.
        const string typed = "Sorry {{ClientFristName}}, rebook: {{CancelUrl}} or {{BookingUrl}}";

        var preview = await PostAsync(client, CancelledSms + "/preview", new() { ["body"] = typed });

        preview.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await preview.Content.ReadAsStringAsync());
        html.ShouldContain("data-testid=\"preview-body\">Sorry <mark class=\"tag-unknown\">{{ClientFristName}} <span class=\"tag-unknown-note\">unknown tag</span></mark>, rebook: <mark class=\"tag-unknown\">{{CancelUrl}} <span class=\"tag-unknown-note\">unknown tag</span></mark> or <mark class=\"tag-value\">");

        var save = await PostAsync(client, CancelledSms, new() { ["body"] = typed });
        save.StatusCode.ShouldBe(HttpStatusCode.OK);
        var refused = WebUtility.HtmlDecode(await save.Content.ReadAsStringAsync());
        refused.ShouldContain("This message cannot use {{ClientFristName}}, {{CancelUrl}}.", Case.Sensitive, "saving refuses exactly what the preview marked");
        Count(refused, "class=\"tag-unknown\"").ShouldBe(2);
    }

    [Fact]
    public async Task The_subject_of_an_email_has_its_own_preview_with_unknown_tags_marked()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var preview = await PostAsync(client, ConfirmationEmail + "/preview", new() { ["subject"] = "{{SalonName}}: code {{Code}}", ["body"] = "Hi {{ClientFirstName}}" });

        var html = WebUtility.HtmlDecode(await preview.Content.ReadAsStringAsync());
        html.ShouldContain("data-testid=\"preview-subject\"><mark class=\"tag-value\">Studio 20</mark>: code <mark class=\"tag-unknown\">{{Code}} <span class=\"tag-unknown-note\">unknown tag</span></mark><");
        html.ShouldContain(">Example subject<");
        html.ShouldContain("data-testid=\"preview-body\">Hi <mark class=\"tag-value\">Olivia</mark><");
    }

    [Fact]
    public async Task The_example_is_counted_after_the_values_are_filled_in()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        // 140 letters, the salon's name and the code read as 162 characters typed (two texts), but as 156 once
        // "Studio 20" and the six-digit example are in: one text.
        var preview = await PostAsync(client, ListPath + "/Booking.Code/sms/preview", new() { ["body"] = new string('a', 140) + "{{SalonName}} {{Code}}" });

        var html = WebUtility.HtmlDecode(await preview.Content.ReadAsStringAsync());
        html.ShouldContain("data-testid=\"preview-length\" data-segments=\"1\">Texts needed: 1. Characters: about 156.<");
    }

    [Fact]
    public async Task Putting_back_the_standard_text_is_easy_to_find_and_asks_first()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        (await PostAsync(client, ConfirmationSms, new() { ["body"] = "See you, {{ClientFirstName}}." })).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var html = await GetAsync(client, ConfirmationSms);

        html.ShouldContain("href=\"#standard\" class=\"btn-link\" data-testid=\"reset-jump\">Put back the standard text</a>");
        html.ShouldContain("<section id=\"standard\"");
        html.ShouldContain("<details class=\"confirm\" data-testid=\"confirm-reset\">");
        html.ShouldContain("data-testid=\"reset-message\">Put back the standard text</summary>");
        html.ShouldContain($"action=\"{ConfirmationSms}/reset\"");
        html.ShouldContain("<p>Your own wording is deleted and the standard text is used again.</p>");
        html.ShouldContain("data-testid=\"reset-yes\">Yes, put it back</button>");

        using var polish = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        (await PostAsync(polish, ConfirmationSms, new() { ["body"] = "Do zobaczenia, {{ClientFirstName}}." })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var polishHtml = await GetAsync(polish, ConfirmationSms);
        polishHtml.ShouldContain("data-testid=\"reset-message\">Przywróć treść standardową</summary>");
        polishHtml.ShouldContain("data-testid=\"reset-yes\">Tak, przywróć</button>");
    }

    [GeneratedRegex("data-tag=\"(?<name>[A-Za-z]+)\"")]
    private static partial Regex ChipPattern();

    private static int Count(string html, string text)
    {
        var count = 0;
        for (var at = html.IndexOf(text, StringComparison.Ordinal); at >= 0; at = html.IndexOf(text, at + text.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Posts with the token of the edit page.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetAsync(client, ConfirmationSms));
        return await client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(fields));
    }
}