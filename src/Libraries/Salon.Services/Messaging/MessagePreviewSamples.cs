using System.Globalization;
using NodaTime;
using Salon.Core.Booking;
using Salon.Core.Messaging;
using Salon.Services.Notifications;

namespace Salon.Services.Messaging;

/// <summary>The made-up client, service and person the messages page fills its examples with, in the page's language.</summary>
public sealed record MessageSamplePeople(string ClientFirstName, string ServiceName, string StaffName);

/// <summary>
/// The example values of every placeholder a message can use (ADR 0030, ADR 0098). Pure: the caller passes the clock
/// and the salon. Values are of realistic length, so the count of texts is close to what a client's message costs: the
/// salon's own name, a visit tomorrow at ten written the way real messages write it, kept until two hours before, the
/// salon's real addresses with a link token as long as a real one, and a code of the real length.
/// </summary>
public static class MessagePreviewSamples
{
    /// <summary>A visit tomorrow at this time of day.</summary>
    public static readonly LocalTime VisitTime = new(10, 0);

    /// <summary>How long before the visit the example keeps the time for a waiting client.</summary>
    public static readonly Duration HeldBefore = Duration.FromHours(2);

    /// <summary>Stands in for the private part of a client's link; as long as a real one.</summary>
    public static string SampleLinkToken { get; } = string.Concat(Enumerable.Repeat("example", 7))[..PublicBookingToken.Length];

    /// <summary>Stands in for a verification code; as long as a real one.</summary>
    public static string SampleCode { get; } = new('1', PhoneVerificationRules.CodeLength);

    public static MessageTokens For(
        string salonName,
        MessageSamplePeople people,
        Instant now,
        DateTimeZone zone,
        CultureInfo culture,
        string cancelUrl,
        string bookingUrl)
    {
        ArgumentNullException.ThrowIfNull(salonName);
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(cancelUrl);
        ArgumentNullException.ThrowIfNull(bookingUrl);

        var start = now.InZone(zone).Date.PlusDays(1).At(VisitTime).InZoneLeniently(zone).ToInstant();
        return MessageTokens.Empty
            .Add("SalonName", salonName)
            .Add("ClientFirstName", people.ClientFirstName)
            .Add("ServiceName", people.ServiceName)
            .Add("StaffName", people.StaffName)
            .Add("AppointmentStart", BookingSmsTokens.FormatStart(start, zone, culture))
            .Add("HeldUntil", BookingSmsTokens.FormatStart(start - HeldBefore, zone, culture))
            .Add("CancelUrl", cancelUrl)
            .Add("BookingUrl", bookingUrl)
            .Add("Code", SampleCode);
    }
}