using Salon.Services.Identity;

namespace Salon.Web.Framework.Identity;

/// <summary>Names and paths of the cookie sign-in (ADR 0009). Slices reference these instead of repeating the strings.</summary>
public static class SalonAuthenticationDefaults
{
    /// <summary>The authentication scheme; equals the authentication type <see cref="ClaimsFactory"/> stamps on the identity.</summary>
    public const string Scheme = ClaimsFactory.AuthenticationType;

    public const string CookieName = "salon.auth";

    public const string LoginPath = "/account/login";

    public const string AccessDeniedPath = "/account/denied";

    public const string ReturnUrlParameter = "returnUrl";

    /// <summary>Name of the rate-limiting policy applied to the login POST.</summary>
    public const string LoginRateLimitPolicy = "login";

    /// <summary>Session claim: Unix seconds of the last time the cookie was checked against the user row.</summary>
    public const string ValidatedAtClaimType = "validated_at";

    /// <summary>Sliding lifetime of the cookie; there is no persistent "remember me" in M0.</summary>
    public static TimeSpan SessionLength { get; } = TimeSpan.FromHours(8);
}