namespace Salon.Web.Models;

/// <param name="TenantName">Set when the request arrived on a salon's subdomain.</param>
public sealed record HomeViewModel(string? TenantName);