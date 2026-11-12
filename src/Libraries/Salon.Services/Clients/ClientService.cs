using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NodaTime;

using Salon.Core.Clients;
using Salon.Core.Clients.Events;
using Salon.Core.Events;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Security;
using Salon.Services.Tenancy;

namespace Salon.Services.Clients;

internal sealed class ClientService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IFieldEncryptor encryptor,
    IEventPublisher publisher,
    IClock clock,
    ILogger<ClientService> logger) : IClientService
{
    public const int MaxSearchTake = 100;
    private const int MinPhoneSearchDigits = 3;
    private const int MaxSearchWords = 3;

    public async Task<Guid> CreateAsync(CreateClientRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Creating a client requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);

        if (!ClientName.TryNormalise(request.FirstName, out var firstName))
        {
            throw new ArgumentException($"First name is required and can have at most {Client.NameMaxLength} characters.", nameof(request));
        }

        if (!ClientName.TryNormalise(request.LastName, out var lastName))
        {
            throw new ArgumentException($"Last name is required and can have at most {Client.NameMaxLength} characters.", nameof(request));
        }

        if (!PhoneNumber.TryNormalise(request.Phone, tenant?.Currency, out var phone))
        {
            throw new ArgumentException("The phone number is not valid; use the country code for numbers from abroad.", nameof(request));
        }

        if (!EmailAddress.TryNormalise(request.Email, out var email))
        {
            throw new ArgumentException($"The email must look like name@domain and have at most {Client.EmailMaxLength} characters.", nameof(request));
        }

        var notes = OptionalText(request.Notes, Client.NotesMaxLength, "Notes");
        var healthNotes = OptionalText(request.HealthNotes, Client.HealthNotesMaxLength, "Health notes");
        var wording = request.MarketingConsent ? RequireWording(request.MarketingConsentWording) : null;

        if (phone is not null && await db.Clients.AnyAsync(c => c.Phone == phone, cancellationToken).ConfigureAwait(false))
        {
            throw new DuplicateClientException(nameof(Client.Phone), phone);
        }

        if (email is not null && await db.Clients.AnyAsync(c => c.Email == email, cancellationToken).ConfigureAwait(false))
        {
            throw new DuplicateClientException(nameof(Client.Email), email);
        }

        var now = clock.GetCurrentInstant();
        var client = new Client
        {
            FirstName = firstName!,
            LastName = lastName!,
            Phone = phone,
            Email = email,
            Notes = notes,
            HealthNotesProtected = healthNotes is null ? null : encryptor.Protect(healthNotes),
            LastPatchTestOn = request.LastPatchTestOn,
            MarketingConsent = request.MarketingConsent,
            MarketingConsentAt = request.MarketingConsent ? now : null,
            MarketingConsentWording = wording,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Clients.Add(client);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new ClientCreated(client.Id, tenantId), cancellationToken).ConfigureAwait(false);
        return client.Id;
    }

    public async Task<IReadOnlyList<ClientSummary>> SearchAsync(string? query, int take = 20, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, MaxSearchTake);

        var clients = db.Clients.AsNoTracking();
        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length > 0)
        {
            clients = LooksLikeANumber(trimmed) ? FilterByPhone(clients, trimmed) : FilterByName(clients, trimmed);
        }

        return await clients
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .Take(take)
            .Select(c => new ClientSummary(c.Id, c.FirstName, c.LastName, c.Phone, c.Email, c.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ClientSummary?> FindByPhoneAsync(string? phone, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Looking up a client requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (!PhoneNumber.TryNormalise(phone, tenant?.Currency, out var normalised) || normalised is null)
        {
            return null;
        }

        return await db.Clients.AsNoTracking()
            .Where(c => c.Phone == normalised)
            .Select(c => new ClientSummary(c.Id, c.FirstName, c.LastName, c.Phone, c.Email, c.IsActive))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ClientCard?> GetCardAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            return null;
        }

        string? healthNotes = null;
        var unreadable = false;
        if (client.HealthNotesProtected is not null && !encryptor.TryUnprotect(client.HealthNotesProtected, out healthNotes))
        {
            // The id only: the cipher text is useless to a reader and the plain text is exactly what must not be logged.
            logger.LogWarning("Health notes of client {ClientId} cannot be decrypted; the data-protection key ring has changed", client.Id);
            unreadable = true;
        }

        return new ClientCard(
            client.Id,
            client.FirstName,
            client.LastName,
            client.Phone,
            client.Email,
            client.Notes,
            healthNotes,
            unreadable,
            client.LastPatchTestOn,
            client.MarketingConsent,
            client.MarketingConsentAt,
            client.MarketingConsentWording,
            client.IsActive,
            client.CreatedAt,
            client.UpdatedAt);
    }

    public async Task UpdateHealthNotesAsync(Guid clientId, string? healthNotes, LocalDate? lastPatchTestOn, CancellationToken cancellationToken = default)
    {
        var plain = OptionalText(healthNotes, Client.HealthNotesMaxLength, "Health notes");
        var client = await RequireAsync(clientId, cancellationToken).ConfigureAwait(false);
        client.HealthNotesProtected = plain is null ? null : encryptor.Protect(plain);
        client.LastPatchTestOn = lastPatchTestOn;
        client.UpdatedAt = clock.GetCurrentInstant();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetMarketingConsentAsync(Guid clientId, bool granted, string? wording, CancellationToken cancellationToken = default)
    {
        var recorded = granted ? RequireWording(wording) : null;
        var client = await RequireAsync(clientId, cancellationToken).ConfigureAwait(false);
        var now = clock.GetCurrentInstant();
        client.MarketingConsent = granted;
        client.MarketingConsentAt = granted ? now : null;
        client.MarketingConsentWording = recorded;
        client.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Digits with optional +, spaces, dashes, dots and parentheses, and at least a few digits to match on.</summary>
    private static bool LooksLikeANumber(string query)
        => PhoneNumber.Digits(query).Length >= MinPhoneSearchDigits
           && query.All(c => char.IsAsciiDigit(c) || char.IsWhiteSpace(c) || c is '+' or '-' or '.' or '(' or ')');

    private static IQueryable<Client> FilterByPhone(IQueryable<Client> clients, string query)
    {
        // "07700" typed by a UK receptionist must still find "+447700…", so the trunk zero is tried without as well.
        var digits = PhoneNumber.Digits(query);
        var withoutTrunk = digits.TrimStart('0');
        return withoutTrunk.Length >= MinPhoneSearchDigits && withoutTrunk != digits
            ? clients.Where(c => c.Phone != null && (c.Phone.Contains(digits) || c.Phone.Contains(withoutTrunk)))
            : clients.Where(c => c.Phone != null && c.Phone.Contains(digits));
    }

    private static IQueryable<Client> FilterByName(IQueryable<Client> clients, string query)
    {
        foreach (var word in query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(MaxSearchWords))
        {
            var pattern = LikePrefix(word);
            clients = clients.Where(c => EF.Functions.ILike(c.FirstName, pattern) || EF.Functions.ILike(c.LastName, pattern));
        }

        return clients;
    }

    /// <summary>Escapes the LIKE wildcards so the word is matched literally (Postgres' default escape character is the backslash).</summary>
    private static string LikePrefix(string word)
        => word.Replace("\\", "\\\\", StringComparison.Ordinal)
               .Replace("%", "\\%", StringComparison.Ordinal)
               .Replace("_", "\\_", StringComparison.Ordinal) + "%";

    private static string? OptionalText(string? raw, int maxLength, string what)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentException($"{what} can have at most {maxLength} characters.", nameof(raw));
    }

    private static string RequireWording(string? wording)
    {
        if (string.IsNullOrWhiteSpace(wording))
        {
            throw new ArgumentException("Marketing consent needs the wording the client agreed to.", nameof(wording));
        }

        var trimmed = wording.Trim();
        return trimmed.Length <= Client.MarketingConsentWordingMaxLength
            ? trimmed
            : throw new ArgumentException($"Consent wording can have at most {Client.MarketingConsentWordingMaxLength} characters.", nameof(wording));
    }

    private async Task<Client> RequireAsync(Guid clientId, CancellationToken cancellationToken)
        => await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken).ConfigureAwait(false)
           ?? throw new InvalidOperationException($"Client {clientId} does not exist in the current tenant.");
}