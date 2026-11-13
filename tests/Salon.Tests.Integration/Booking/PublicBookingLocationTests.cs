using Microsoft.Extensions.DependencyInjection;
using Salon.Services.Locations;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Booking;

/// <summary>
/// Where to find the salon (ADR 0027): once the salon has entered its address, every step of the public booking shows
/// it with a maps link and the phone number, and the emails end with it. Without an address nothing is shown and the
/// messages are what they were. Another salon's address never appears.
/// </summary>
public partial class PublicBookingTests
{
    private const string MapHref = "href=\"https://www.google.com/maps/search/?api=1&query=12%20High%20Street%2C%20London%20N1%209GU\"";

    [Fact]
    public async Task Without_an_address_the_booking_pages_show_no_contact_block()
    {
        using var client = factory.ClientFor(HostA);

        (await GetAsync(client, "/book")).ShouldNotContain("data-testid=\"salon-contact\"");
        (await GetAsync(client, DetailsPath(At(10, 0), _annaId))).ShouldNotContain("data-testid=\"salon-contact\"");
    }

    [Fact]
    public async Task With_an_address_every_step_says_where_to_find_the_salon_and_how_to_call_it()
    {
        await SaveLocationAsync(_studio.Id, new LocationDetails(null, "12 High Street", null, "London", "N1 9GU", "020 7946 0020", "hello@studio20.example"));
        // The other salon's address must never show up on this salon's pages.
        await SaveLocationAsync(_polish.Id, new LocationDetails(null, "ul. Piękna 12", null, "Warszawa", "00-549", "22 123 45 67", null));
        using var client = factory.ClientFor(HostA);

        foreach (var path in new[] { "/book", "/services", $"/book/{_serviceId}", DetailsPath(At(10, 0), _annaId) })
        {
            var page = await GetAsync(client, path);
            if (path == $"/book/{_serviceId}")
            {
                // The slot step is about times; the address comes back on the next page.
                page.ShouldNotContain("Piękna", Case.Sensitive, path);
                continue;
            }

            page.ShouldContain("Where to find us", Case.Sensitive, path);
            page.ShouldContain("data-testid=\"salon-contact-address\">12 High Street, London N1 9GU<", Case.Sensitive, path);
            page.ShouldContain(MapHref, Case.Sensitive, path);
            page.ShouldContain("href=\"tel:02079460020\"", Case.Sensitive, path);
            page.ShouldContain("Call 020 7946 0020", Case.Sensitive, path);
            page.ShouldContain("href=\"mailto:hello@studio20.example\"", Case.Sensitive, path);
            page.ShouldNotContain("Piękna", Case.Sensitive, path);
        }

        var (appointment, donePath) = await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        var done = await GetAsync(client, donePath);
        done.ShouldContain("data-testid=\"salon-contact-address\">12 High Street, London N1 9GU<");
        done.ShouldContain(MapHref);
        (await GetAsync(client, $"/book/cancel/{appointment.PublicToken}")).ShouldContain("href=\"tel:02079460020\"");
        (await GetAsync(client, $"/book/change/{appointment.PublicToken}")).ShouldContain("href=\"tel:02079460020\"");
    }

    [Fact]
    public async Task The_polish_salon_shows_its_own_address_in_polish()
    {
        await SaveLocationAsync(_polish.Id, new LocationDetails("Salon PL Centrum", "ul. Piękna 12", null, "Warszawa", "00-549", "22 123 45 67", null));
        using var client = factory.ClientFor(HostB);

        var page = await GetAsync(client, "/book");

        page.ShouldContain("Gdzie nas znaleźć");
        page.ShouldContain("data-testid=\"salon-contact-name\">Salon PL Centrum<");
        page.ShouldContain("data-testid=\"salon-contact-address\">ul. Piękna 12, 00-549 Warszawa<");
        page.ShouldContain("Otwórz w mapach");
        page.ShouldContain("Zadzwoń: 22 123 45 67");
        page.ShouldNotContain("data-testid=\"salon-contact-email\"");
    }

    [Fact]
    public async Task The_confirmation_email_ends_with_the_address_and_the_text_stays_as_short_as_it_was()
    {
        await SaveLocationAsync(_studio.Id, new LocationDetails(null, "12 High Street", null, "London", "N1 9GU", "020 7946 0020", null));
        using var client = factory.ClientFor(HostA);
        await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        await DispatchAsync();

        var email = factory.Emails.Messages.ShouldHaveSingleItem();
        email.TextBody.ShouldNotBeNull().ShouldEndWith("See you soon.\n\nStudio 20\n12 High Street, London N1 9GU\n020 7946 0020");
        email.HtmlBody.ShouldEndWith("<br>\n<br>\nStudio 20<br>\n12 High Street, London N1 9GU<br>\n020 7946 0020</p>");
        var text = ConfirmationTexts().ShouldHaveSingleItem();
        text.Body.ShouldNotContain("High Street");
    }

    private async Task SaveLocationAsync(Guid tenantId, LocationDetails details)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        await scope.ServiceProvider.GetRequiredService<ILocationService>().SaveMainAsync(details);
    }
}