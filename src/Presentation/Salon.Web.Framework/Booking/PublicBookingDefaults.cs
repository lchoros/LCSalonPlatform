namespace Salon.Web.Framework.Booking;

/// <summary>Paths and policy names of the public booking pages (ADR 0014). Slices and the URL builder reference these instead of repeating the strings.</summary>
public static class PublicBookingDefaults
{
    public const string BookPath = "/book";

    /// <summary>Rate-limiting policy on the details POST (the one that sends an SMS code): per address, see <see cref="PublicBookingOptions.SmsCodeRateLimit"/>.</summary>
    public const string SmsCodeRateLimitPolicy = "sms-code";

    /// <summary>Rate-limiting policy on the verify POST: per address, see <see cref="PublicBookingOptions.CodeVerifyRateLimit"/>.</summary>
    public const string CodeVerifyRateLimitPolicy = "code-verify";

    public static string CancelPath(string publicToken) => $"{BookPath}/cancel/{Uri.EscapeDataString(publicToken)}";

    /// <summary>Prefix of the short address that messages carry (ADR 0031): <c>/v/{token}</c>, "v" for visit.</summary>
    public const string ShortLinkPrefix = "/v";

    /// <summary>
    /// The address put into texts and emails. It leads to <see cref="CancelPath"/>; it exists only to be short, so a
    /// confirmation with its link fits in one text.
    /// </summary>
    public static string ShortLinkPath(string publicToken) => $"{ShortLinkPrefix}/{Uri.EscapeDataString(publicToken)}";
}