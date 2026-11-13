using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NodaTime;

using Salon.Core.Locations;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Tenancy;

namespace Salon.Services.Locations;

/// <summary>
/// Reads and writes the salon's main location through the tenant filter (ADR 0027): the first active row by sort
/// order. One small indexed read per page that shows the address.
/// </summary>
internal sealed class LocationService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClock clock,
    ILogger<LocationService> logger) : ILocationService
{
    public async Task<SalonLocation?> GetMainAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await TenantAsync(cancellationToken).ConfigureAwait(false);
        var location = await Main(db.Locations.AsNoTracking()).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return location is null ? null : ToModel(location, tenant);
    }

    public async Task<SalonLocation> SaveMainAsync(LocationDetails details, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        var addressLine1 = Required(details.AddressLine1, Location.AddressLineMaxLength, nameof(details.AddressLine1));
        var city = Required(details.City, Location.CityMaxLength, nameof(details.City));
        var postcode = Required(details.Postcode, Location.PostcodeMaxLength, nameof(details.Postcode));
        var name = Optional(details.Name, Location.NameMaxLength, nameof(details.Name));
        var addressLine2 = Optional(details.AddressLine2, Location.AddressLineMaxLength, nameof(details.AddressLine2));
        var email = Optional(details.Email, Location.EmailMaxLength, nameof(details.Email));
        var phone = Optional(details.Phone, Location.PhoneMaxLength, nameof(details.Phone));
        if (phone is not null && !LocationRules.IsPhone(phone))
        {
            throw new ArgumentException("The phone number is not a phone number.", nameof(details));
        }

        var tenant = await TenantAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.GetCurrentInstant();
        var location = await Main(db.Locations).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (location is null)
        {
            location = new Location { TenantId = tenant.Id, AddressLine1 = addressLine1, City = city, Postcode = postcode, CreatedAt = now };
            db.Locations.Add(location);
        }

        location.Name = name;
        location.AddressLine1 = addressLine1;
        location.AddressLine2 = addressLine2;
        location.City = city;
        location.Postcode = postcode;
        location.Phone = phone;
        location.Email = email;
        location.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Main location {LocationId} of tenant {TenantId} saved", location.Id, tenant.Id);
        return ToModel(location, tenant);
    }

    private static IQueryable<Location> Main(IQueryable<Location> locations)
        => locations.Where(l => l.IsActive).OrderBy(l => l.SortOrder).ThenBy(l => l.CreatedAt).ThenBy(l => l.Id);

    private static SalonLocation ToModel(Location location, TenantInfo tenant)
        => new(
            location.Id,
            location.Name,
            location.AddressLine1,
            location.AddressLine2,
            location.City,
            location.Postcode,
            location.Phone,
            location.Email,
            LocationRules.OneLine(location.AddressLine1, location.AddressLine2, location.City, location.Postcode, LocationRules.PostcodeFirst(tenant.Culture)));

    private static string Required(string? value, int maxLength, string field)
        => Optional(value, maxLength, field) ?? throw new ArgumentException($"{field} is required.", field);

    private static string? Optional(string? value, int maxLength, string field)
    {
        var text = LocationRules.Normalize(value);
        return text is { Length: var length } && length > maxLength
            ? throw new ArgumentException($"{field} is longer than {maxLength} characters.", field)
            : text;
    }

    private async Task<TenantInfo> TenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("A location belongs to a salon, but no tenant is set in the current scope.");
        return await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
    }
}