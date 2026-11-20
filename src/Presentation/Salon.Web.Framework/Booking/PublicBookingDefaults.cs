namespace Salon.Web.Framework.Booking;

/// <summary>Paths and policy names of the public booking pages (ADR 0014). Slices and the URL builder reference these instead of repeating the strings.</summary>
public static class PublicBookingDefaults
{
    public const string BookPath = "/book";

    /// <summary>Rate-limiting policy on the details POST (the one that sends an SMS code): per address, see <see cref="PublicBookingOptions.SmsCodeRateLimit"/>.</summary>
    public const string SmsCodeRateLimitPolicy = "sms-code";

    /// <summary>Rate-limiting policy on the verify POST: per address, see <see cref="PublicBookingOptions.CodeVerifyRateLimit"/>.</summary>
    public const string CodeVerifyRateLimitPolicy = "code-verify";

    /// <summary>Rate-limiting policy on the pay step (ADR 0082), each of which opens a session at the payment gateway: per address, see <see cref="PublicBookingOptions.DepositPayRateLimit"/>.</summary>
    public const string DepositPayRateLimitPolicy = "deposit-pay";

    /// <summary>The confirmation of an online booking (ADR 0014).</summary>
    public static string DonePath(Guid appointmentId) => $"{BookPath}/done/{appointmentId}";

    /// <summary>The pay step of an online booking that asks for a deposit (ADR 0082): it sends the client on to the payment gateway.</summary>
    public static string PayDepositPath(Guid appointmentId) => $"{BookPath}/pay/{appointmentId}";

    /// <summary>Where the payment gateway sends the client back after paying (ADR 0082); the gateway adds its session id.</summary>
    public static string PayDepositReturnPath(Guid appointmentId) => $"{BookPath}/pay/{appointmentId}/return";

    public static string CancelPath(string publicToken) => $"{BookPath}/cancel/{Uri.EscapeDataString(publicToken)}";

    /// <summary>Prefix of the short address that messages carry (ADR 0031): <c>/v/{token}</c>, "v" for visit.</summary>
    public const string ShortLinkPrefix = "/v";

    /// <summary>
    /// The address put into texts and emails. It leads to <see cref="CancelPath"/>; it exists only to be short, so a
    /// confirmation with its link fits in one text.
    /// </summary>
    public static string ShortLinkPath(string publicToken) => $"{ShortLinkPrefix}/{Uri.EscapeDataString(publicToken)}";
}