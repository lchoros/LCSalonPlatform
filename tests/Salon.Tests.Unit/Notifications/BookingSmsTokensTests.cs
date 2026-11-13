using System.Globalization;
using NodaTime;
using Salon.Core.Messaging;
using Salon.Services.Notifications;

namespace Salon.Tests.Unit.Notifications;

/// <summary>The confirmation SMS tokens (ADR 0014) carry exactly the catalogue's names, with the start in the salon's zone and culture.</summary>
public class BookingSmsTokensTests
{
    // 10:00 London in November is 10:00 UTC; 10:00 Warsaw is 09:00 UTC.
    private static readonly Instant Start = Instant.FromUtc(2026, 11, 17, 10, 0);
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly DateTimeZone Warsaw = DateTimeZoneProviders.Tzdb["Europe/Warsaw"];

    [Fact]
    public void Formats_the_start_in_the_salon_zone_and_culture()
    {
        BookingSmsTokens.FormatStart(Start, London, CultureInfo.GetCultureInfo("en-GB")).ShouldBe("Tue 17 Nov, 10:00");
        BookingSmsTokens.FormatStart(Start, Warsaw, CultureInfo.GetCultureInfo("pl-PL")).ShouldBe("wt. 17 lis, 11:00");
    }

    [Fact]
    public void Confirmation_tokens_match_the_catalogue_exactly()
    {
        var tokens = BookingSmsTokens.Confirmation("Studio 20", "Olivia", "Cut & blow-dry", "Anna Kowalska", Start, London, CultureInfo.GetCultureInfo("en-GB"), "https://studio-20.salon.example/book/cancel/tok");

        var definition = MessageTemplateCatalog.Get(MessageTemplateCatalog.BookingConfirmation);
        tokens.Names.Order(StringComparer.Ordinal).ShouldBe(definition.TokenNames.Order(StringComparer.Ordinal));
        tokens.TryGet("AppointmentStart", out var start).ShouldBeTrue();
        start.ShouldBe("Tue 17 Nov, 10:00");
        tokens.TryGet("CancelUrl", out var url).ShouldBeTrue();
        url.ShouldBe("https://studio-20.salon.example/book/cancel/tok");
    }

    [Fact]
    public void The_rendered_confirmation_carries_the_service_time_and_link()
    {
        var tokens = BookingSmsTokens.Confirmation("Studio 20", "Olivia", "Cut & blow-dry", "Anna Kowalska", Start, London, CultureInfo.GetCultureInfo("en-GB"), "https://s.example/c/tok");

        var rendered = MessageTemplateRenderer.Render(
            "Hi {{ClientFirstName}}, your {{ServiceName}} at {{SalonName}} is booked for {{AppointmentStart}}. Need to cancel? {{CancelUrl}}",
            tokens);

        rendered.IsComplete.ShouldBeTrue();
        rendered.Text.ShouldBe("Hi Olivia, your Cut & blow-dry at Studio 20 is booked for Tue 17 Nov, 10:00. Need to cancel? https://s.example/c/tok");
    }

    [Fact]
    public void Code_tokens_match_the_catalogue_and_carry_the_code()
    {
        var tokens = BookingSmsTokens.Code("Studio 20", "482913");

        var definition = MessageTemplateCatalog.Get(MessageTemplateCatalog.BookingCode);
        tokens.Names.Order(StringComparer.Ordinal).ShouldBe(definition.TokenNames.Order(StringComparer.Ordinal));
        MessageTemplateRenderer.Render("{{SalonName}}: your code is {{Code}}.", tokens).Text.ShouldBe("Studio 20: your code is 482913.");
    }
}