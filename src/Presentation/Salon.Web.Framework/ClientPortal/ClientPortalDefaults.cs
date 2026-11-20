using NodaTime;

namespace Salon.Web.Framework.ClientPortal;

/// <summary>
/// Addresses, the cookie name and the lifetimes of a client's own pages (ADR 0083). The slices and the layout
/// reference these instead of repeating the strings; nothing personal is ever part of an address.
/// </summary>
public static class ClientPortalDefaults
{
    /// <summary>The client's own cookie. It is not the staff cookie (<c>salon.auth</c>) and is never read as one.</summary>
    public const string CookieName = "salon.client";

    /// <summary>The upcoming visits; also the path the cookie is bound to, so the browser sends it nowhere else.</summary>
    public const string VisitsPath = "/my-bookings";

    public const string SignInPath = VisitsPath + "/sign-in";

    public const string CodePathPrefix = VisitsPath + "/code";

    public const string SignOutPath = VisitsPath + "/sign-out";

    /// <summary>The session ends after this long without a visit to the pages.</summary>
    public static Duration IdleLifetime { get; } = Duration.FromMinutes(30);

    /// <summary>The session ends this long after the code was entered, however busy it has been.</summary>
    public static Duration AbsoluteLifetime { get; } = Duration.FromHours(12);

    /// <summary>The page that takes the code; the id is the verification row's, which says nothing about anybody.</summary>
    public static string CodePath(Guid verificationId) => $"{CodePathPrefix}/{verificationId}";
}