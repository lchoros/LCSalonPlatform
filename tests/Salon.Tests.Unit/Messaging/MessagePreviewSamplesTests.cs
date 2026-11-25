using System.Globalization;
using NodaTime;
using Salon.Core.Booking;
using Salon.Core.Messaging;
using Salon.Services.Messaging;

namespace Salon.Tests.Unit.Messaging;

/// <summary>
/// The made-up details of the messages page (ADR 0098): an example for every tag any message offers, a visit tomorrow
/// at ten written in the salon's own format, and values as long as real ones so the count of texts is honest.
/// </summary>
public class MessagePreviewSamplesTests
{
    // Monday 16 November 2026, 21:30 in London and 22:30 in Warsaw.
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 16, 21, 30);
    private static readonly MessageSamplePeople People = new("Olivia", "Cut and blow-dry", "Emma");

    private static MessageTokens Build(string zone = "Europe/London", string culture = "en-GB")
        => MessagePreviewSamples.For("Studio 20", People, Now, DateTimeZoneProviders.Tzdb[zone], CultureInfo.GetCultureInfo(culture), "https://studio-20.example/v/" + MessagePreviewSamples.SampleLinkToken, "https://studio-20.example/book");

    [Fact]
    public void Every_tag_any_message_offers_has_an_example()
    {
        var samples = Build();

        foreach (var name in MessageTemplateCatalog.All.SelectMany(d => d.TokenNames).Distinct())
        {
            samples.TryGet(name, out var value).ShouldBeTrue(name);
            value.ShouldNotBeNullOrWhiteSpace(name);
        }
    }

    [Fact]
    public void The_people_and_the_salon_are_the_ones_given()
    {
        var samples = Build();

        Value(samples, "SalonName").ShouldBe("Studio 20");
        Value(samples, "ClientFirstName").ShouldBe("Olivia");
        Value(samples, "ServiceName").ShouldBe("Cut and blow-dry");
        Value(samples, "StaffName").ShouldBe("Emma");
        Value(samples, "BookingUrl").ShouldBe("https://studio-20.example/book");
    }

    [Fact]
    public void The_visit_is_tomorrow_at_ten_in_the_salons_zone_and_format_and_is_kept_until_two_hours_before()
    {
        var london = Build();
        Value(london, "AppointmentStart").ShouldBe("Tue 17 Nov, 10:00");
        Value(london, "HeldUntil").ShouldBe("Tue 17 Nov, 08:00");

        // 22:30 in Warsaw is still Monday there too; the Polish salon writes it in Polish.
        var warsaw = Build("Europe/Warsaw", "pl-PL");
        Value(warsaw, "AppointmentStart").ShouldBe("wt. 17 lis, 10:00");
    }

    [Fact]
    public void The_link_and_the_code_are_as_long_as_real_ones()
    {
        MessagePreviewSamples.SampleLinkToken.Length.ShouldBe(PublicBookingToken.Length);
        Value(Build(), "Code").ShouldBe(new string('1', PhoneVerificationRules.CodeLength));
        Value(Build(), "CancelUrl").ShouldEndWith("/v/" + MessagePreviewSamples.SampleLinkToken);
    }

    private static string Value(MessageTokens tokens, string name)
    {
        tokens.TryGet(name, out var value).ShouldBeTrue(name);
        return value;
    }
}