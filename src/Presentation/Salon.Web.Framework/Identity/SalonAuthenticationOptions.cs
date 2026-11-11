namespace Salon.Web.Framework.Identity;

/// <summary>
/// Host-level sign-in settings (section "Auth"). The defaults are the ADR 0009 values; tests lower them to exercise the
/// mechanisms without waiting. Per-tenant policy (password rules, MFA) is an M2 setting, not here.
/// </summary>
public sealed class SalonAuthenticationOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// How long a cookie may go without being checked against the user row (active, same security stamp). Shorter means
    /// a deactivation or role change takes effect sooner at the cost of one lookup per interval per session.
    /// </summary>
    public TimeSpan ValidationInterval { get; set; } = TimeSpan.FromMinutes(5);

    public LoginRateLimitOptions LoginRateLimit { get; set; } = new();
}

/// <summary>Fixed window per remote address for the login POST.</summary>
public sealed class LoginRateLimitOptions
{
    public int PermitLimit { get; set; } = 10;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}