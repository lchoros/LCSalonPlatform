namespace Salon.Web.Models;

/// <param name="TenantName">Set when the request arrived on a salon's subdomain.</param>
/// <param name="SignUpAvailable">True on the bare base domain while <c>Features:SignUp</c> is on (ADR 0075).</param>
public sealed record HomeViewModel(string? TenantName, bool SignUpAvailable = false);