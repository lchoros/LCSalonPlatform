using System.Globalization;

using NodaTime;
using NodaTime.Text;

using Salon.Core.Messaging;

namespace Salon.Services.Notifications;

/// <summary>
/// Builds the token sets for booking messages (ADR 0008 tokens, ADR 0014 confirmation). Pure: values are already
/// formatted strings, dates in the salon's zone and culture ("Tue 18 Nov, 10:00" / "wt. 18 lis, 10:00").
/// </summary>
public static class BookingSmsTokens
{
    private const string StartPattern = "ddd d MMM, HH:mm";

    public static string FormatStart(Instant start, DateTimeZone zone, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(culture);
        return LocalDateTimePattern.Create(StartPattern, culture).Format(start.InZone(zone).LocalDateTime);
    }

    /// <summary>Tokens of <see cref="MessageTemplateCatalog.BookingConfirmation"/>.</summary>
    public static MessageTokens Confirmation(
        string salonName,
        string clientFirstName,
        string serviceName,
        string staffName,
        Instant start,
        DateTimeZone zone,
        CultureInfo culture,
        string cancelUrl)
    {
        ArgumentNullException.ThrowIfNull(salonName);
        ArgumentNullException.ThrowIfNull(clientFirstName);
        ArgumentNullException.ThrowIfNull(serviceName);
        ArgumentNullException.ThrowIfNull(staffName);
        ArgumentNullException.ThrowIfNull(cancelUrl);

        return MessageTokens.Empty
            .Add("SalonName", salonName)
            .Add("ClientFirstName", clientFirstName)
            .Add("ServiceName", serviceName)
            .Add("StaffName", staffName)
            .Add("AppointmentStart", FormatStart(start, zone, culture))
            .Add("CancelUrl", cancelUrl);
    }

    /// <summary>Tokens of <see cref="MessageTemplateCatalog.BookingCode"/>. The code is in the caller's memory only; it is never logged.</summary>
    /// <summary>The tokens of <c>Booking.Cancelled</c>: the visit that is off and where to book a new one.</summary>
    public static MessageTokens Cancelled(
        string salonName,
        string clientFirstName,
        string serviceName,
        string staffName,
        Instant start,
        DateTimeZone zone,
        CultureInfo culture,
        string bookingUrl)
    {
        ArgumentNullException.ThrowIfNull(salonName);
        ArgumentNullException.ThrowIfNull(clientFirstName);
        ArgumentNullException.ThrowIfNull(serviceName);
        ArgumentNullException.ThrowIfNull(staffName);
        ArgumentNullException.ThrowIfNull(bookingUrl);

        return MessageTokens.Empty
            .Add("SalonName", salonName)
            .Add("ClientFirstName", clientFirstName)
            .Add("ServiceName", serviceName)
            .Add("StaffName", staffName)
            .Add("AppointmentStart", FormatStart(start, zone, culture))
            .Add("BookingUrl", bookingUrl);
    }

    public static MessageTokens Code(string salonName, string code)
    {
        ArgumentNullException.ThrowIfNull(salonName);
        ArgumentException.ThrowIfNullOrEmpty(code);
        return MessageTokens.Empty.Add("SalonName", salonName).Add("Code", code);
    }
}