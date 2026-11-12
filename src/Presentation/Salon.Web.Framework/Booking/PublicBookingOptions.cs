namespace Salon.Web.Framework.Booking;

/// <summary>
/// Host-level anti-abuse limits of the public booking flow (section "PublicBooking", ADR 0014). The per-phone limit
/// (three codes an hour) is a domain rule in <c>PhoneVerificationRules</c>, not configuration; these are the per-address
/// limits the framework rate limiter enforces before the action runs. Tests raise them for the shared host.
/// </summary>
public sealed class PublicBookingOptions
{
    public const string SectionName = "PublicBooking";

    /// <summary>Details POSTs (each may send one SMS) per remote address.</summary>
    public FixedWindowLimit SmsCodeRateLimit { get; set; } = new() { PermitLimit = 10, Window = TimeSpan.FromHours(1) };

    /// <summary>Code attempts per remote address.</summary>
    public FixedWindowLimit CodeVerifyRateLimit { get; set; } = new() { PermitLimit = 20, Window = TimeSpan.FromHours(1) };
}

/// <summary>A fixed window per remote address.</summary>
public sealed class FixedWindowLimit
{
    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; }
}