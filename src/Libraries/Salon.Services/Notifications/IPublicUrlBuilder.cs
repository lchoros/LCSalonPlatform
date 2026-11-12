namespace Salon.Services.Notifications;

/// <summary>
/// Absolute links to a salon's public pages for messages that leave the request (SMS, email, jobs), where no
/// <c>HttpContext</c> exists (ADR 0014). Implemented in Salon.Web.Framework from the host's tenancy options (base
/// domain, public scheme); the service layer only knows the contract, like the password hasher (ADR 0009).
/// </summary>
public interface IPublicUrlBuilder
{
    /// <summary>The salon's booking start page, e.g. <c>https://studio-20.salon.example/book</c>.</summary>
    string BookingUrl(string tenantSlug);

    /// <summary>The cancel page for one appointment, addressed by its public token.</summary>
    string CancelBookingUrl(string tenantSlug, string publicToken);
}