using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Booking;
using Salon.Core.Messaging;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Booking;

/// <summary>
/// Short links (ADR 0031): messages carry <c>/v/{token}</c> with a 22-character token, so the standard confirmation
/// with its link fits in one text. The short address forwards to the page where the visit is changed or cancelled.
/// Links given before the change keep working.
/// </summary>
public partial class PublicBookingTests
{
    [Fact]
    public async Task The_link_in_the_confirmation_is_short_and_leads_to_the_clients_visit()
    {
        using var client = factory.ClientFor(HostA);
        var (appointment, _) = await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        await DispatchAsync();

        var token = appointment.PublicToken.ShouldNotBeNull();
        token.Length.ShouldBe(PublicBookingToken.Length);
        var text = ConfirmationTexts().ShouldHaveSingleItem();
        text.Body.ShouldEndWith($"https://studio-20.localhost/v/{token}");

        var forward = await client.GetAsync(new Uri($"/v/{token}", UriKind.Relative));
        forward.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        forward.Headers.Location!.OriginalString.ShouldBe($"/book/cancel/{token}");
        var page = await GetAsync(client, forward.Headers.Location.OriginalString);
        page.ShouldContain("Cut");
        page.ShouldContain("Anna");
    }

    [Fact]
    public async Task The_standard_confirmation_with_its_link_is_one_text()
    {
        using var client = factory.ClientFor(HostA);
        await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        await DispatchAsync();

        var text = ConfirmationTexts().ShouldHaveSingleItem();

        // A longer address, as in production, and a longer service name still leave room: 160 is the limit of one text.
        var length = SmsLengthRules.Measure(text.Body);
        length.IsUnicode.ShouldBeFalse();
        length.Segments.ShouldBe(1);
        var inProduction = text.Body
            .Replace("https://studio-20.localhost/", "https://studio-20.salonplatform.co.uk/", StringComparison.Ordinal)
            .Replace("your Cut at", "your Cut and blow-dry at", StringComparison.Ordinal);
        SmsLengthRules.Measure(inProduction).Segments.ShouldBe(1, $"{inProduction.Length} characters: {inProduction}");
    }

    [Fact]
    public async Task A_link_given_before_the_links_got_shorter_still_opens_the_visit()
    {
        using var client = factory.ClientFor(HostA);
        var (appointment, _) = await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        var old = string.Concat(Enumerable.Repeat("OldLink-", 6))[..PublicBookingToken.LegacyLength];
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments
                .Where(a => a.Id == appointment.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.PublicToken, old));
        }

        (await GetAsync(client, $"/book/cancel/{old}")).ShouldContain("data-testid=\"cancel-form\"");
        var forward = await client.GetAsync(new Uri($"/v/{old}", UriKind.Relative));
        forward.Headers.Location!.OriginalString.ShouldBe($"/book/cancel/{old}");
        (await GetAsync(client, $"/book/change/{old}")).ShouldContain("Cut");
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("not-a-link")]
    [InlineData("a%2Fb")]
    public async Task A_short_link_nobody_was_given_forwards_to_an_explanation_on_this_site(string token)
    {
        using var client = factory.ClientFor(HostA);

        var forward = await client.GetAsync(new Uri($"/v/{token}", UriKind.Relative));

        forward.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var target = forward.Headers.Location!.OriginalString;
        target.ShouldStartWith("/book/cancel/");
        var page = await GetAsync(client, target);
        page.ShouldNotContain("data-testid=\"cancel-form\"");
        page.ShouldContain("data-testid=\"cancel-result\"");
    }

    [Fact]
    public async Task A_short_link_means_nothing_in_another_salon()
    {
        using var client = factory.ClientFor(HostA);
        var (appointment, _) = await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        using var other = factory.ClientFor(HostB);

        var forward = await other.GetAsync(new Uri($"/v/{appointment.PublicToken}", UriKind.Relative));

        var page = await GetAsync(other, forward.Headers.Location!.OriginalString);
        page.ShouldNotContain("Anna");
        page.ShouldNotContain("data-testid=\"cancel-form\"");
    }
}