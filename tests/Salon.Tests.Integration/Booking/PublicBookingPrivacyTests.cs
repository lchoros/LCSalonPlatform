using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Salon.Services.Locations;
using Salon.Services.Privacy;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Booking;

/// <summary>
/// The privacy notice on the public pages (ADR 0063): anybody can read what the salon does with their details before
/// they give them. The standard notice names the salon, says what is kept, why, who sees it, and what the client can
/// ask; every public page and the forms that ask for details link to it; a salon's own text takes its place when
/// the owner says so; each salon has its own, in its language.
/// </summary>
public partial class PublicBookingTests
{
    [Fact]
    public async Task Anybody_can_read_the_standard_notice_which_names_the_salon_and_the_clients_rights()
    {
        await SavePrivacyLocationAsync(_studio.Id, new LocationDetails(null, "12 High Street", null, "London", "N1 9GU", "020 7946 0020", "hello@studio20.example"));
        using var visitor = factory.ClientFor(HostA);

        var notice = await GetAsync(visitor, "/privacy");

        notice.ShouldContain("data-testid=\"privacy-notice\" data-own=\"false\"");
        notice.ShouldContain("Studio 20, 12 High Street, London N1 9GU, decides how your details are used.");
        foreach (var said in new[]
                 {
                     "Your name and phone number, and your email if you give it.",
                     "If you tell us about an allergy, a skin condition or a patch test, we keep that too",
                     "To send you offers, only if you said yes.",
                     "The people who work at Studio 20.",
                     "We do not sell your details, and they are not listed on any marketplace.",
                     "If you ask us to delete them, we do.",
                     "For a copy of what we hold about you.",
                     "To delete your details.",
                     "We answer within a month.",
                     "Information Commissioner's Office at ico.org.uk",
                     "Call us on 020 7946 0020 or write to hello@studio20.example.",
                     "Last updated",
                 })
        {
            notice.ShouldContain(said);
        }

        // The platform's own address has no salon, so it has no notice.
        using var platform = factory.ClientFor("localhost");
        (await platform.GetAsync(new Uri("/privacy", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Without_an_address_or_a_number_the_notice_still_names_the_salon_and_says_where_to_ask()
    {
        using var visitor = factory.ClientFor(HostA);

        var notice = await GetAsync(visitor, "/privacy");

        notice.ShouldContain("Studio 20 decides how your details are used.");
        notice.ShouldContain("data-testid=\"privacy-contact-line\">Ask at the salon.<");
    }

    [Fact]
    public async Task Every_public_page_and_both_forms_that_ask_for_details_link_to_the_notice()
    {
        using var visitor = factory.ClientFor(HostA);

        foreach (var path in new[] { "/book", "/services", $"/book/{_serviceId}", DetailsPath(At(10, 0), _annaId), "/privacy" })
        {
            (await GetAsync(visitor, path)).ShouldContain("href=\"/privacy\" class=\"underline underline-offset-2\" data-testid=\"footer-privacy\"", Case.Sensitive, path);
        }

        var details = await GetAsync(visitor, DetailsPath(At(10, 0), _annaId));
        details.ShouldContain("We use what you type here to book your visit and to contact you about it.");
        details.ShouldContain("data-testid=\"details-privacy-link\">How we use your details</a>");

        // Staff pages are not what a client reads: no link in their footer.
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Salon.Core.Identity.Role.Owner);
        (await GetAsync(owner, "/admin/clients")).ShouldNotContain("data-testid=\"footer-privacy\"");
    }

    [Fact]
    public async Task A_salons_own_text_takes_the_place_of_the_standard_notice_for_that_salon_only()
    {
        const string own = "We are a small salon.\nWe keep your number to call you.\n\nAsk Dana at the desk for anything about your details.";
        (await PrivacyAsync(_studio.Id, s => s.SaveAsync(new PrivacyNoticeChoice(true, own)))).ShouldBe(PrivacyNoticeOutcome.Saved);
        using var visitor = factory.ClientFor(HostA);

        var notice = await GetAsync(visitor, "/privacy");

        notice.ShouldContain("data-testid=\"privacy-notice\" data-own=\"true\"");
        notice.ShouldContain("We are a small salon.\nWe keep your number to call you.");
        notice.ShouldContain("Ask Dana at the desk for anything about your details.");
        notice.ShouldNotContain("data-testid=\"privacy-what\"");
        notice.ShouldNotContain("ico.org.uk");
        notice.ShouldContain("Studio 20 decides how your details are used.", Case.Sensitive, "the salon is still named above the owner's text");

        // The other salon still shows the standard notice, in its own language and with its own regulator.
        using var polish = factory.ClientFor(HostB);
        var theirs = await GetAsync(polish, "/privacy");
        theirs.ShouldContain("data-own=\"false\"");
        theirs.ShouldContain("Salon PL decyduje o tym, jak używane są Twoje dane.");
        theirs.ShouldContain("uodo.gov.pl");
        theirs.ShouldNotContain("ico.org.uk");
        theirs.ShouldNotContain("Dana");

        // Switching back shows the standard notice again and keeps the text for later.
        (await PrivacyAsync(_studio.Id, s => s.SaveAsync(new PrivacyNoticeChoice(false, own)))).ShouldBe(PrivacyNoticeOutcome.Saved);
        (await GetAsync(visitor, "/privacy")).ShouldContain("data-own=\"false\"");
        (await PrivacyAsync(_studio.Id, s => s.GetChoiceAsync())).ShouldBe(new PrivacyNoticeChoice(false, own));
    }

    [Fact]
    public async Task Markup_in_a_salons_own_text_is_shown_as_text()
    {
        (await PrivacyAsync(_studio.Id, s => s.SaveAsync(new PrivacyNoticeChoice(true, "<script>alert(1)</script> & <b>bold</b>")))).ShouldBe(PrivacyNoticeOutcome.Saved);
        using var visitor = factory.ClientFor(HostA);

        using var response = await visitor.GetAsync(new Uri("/privacy", UriKind.Relative));
        var raw = await response.Content.ReadAsStringAsync();

        raw.ShouldNotContain("<script>alert(1)</script>");
        raw.ShouldNotContain("<b>bold</b>");
        WebUtility.HtmlDecode(raw).ShouldContain("<script>alert(1)</script> & <b>bold</b>");
    }

    private async Task SavePrivacyLocationAsync(Guid tenantId, LocationDetails details)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        await scope.ServiceProvider.GetRequiredService<ILocationService>().SaveMainAsync(details);
    }

    private async Task<T> PrivacyAsync<T>(Guid tenantId, Func<IPrivacyNoticeService, Task<T>> act)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await act(scope.ServiceProvider.GetRequiredService<IPrivacyNoticeService>());
    }
}