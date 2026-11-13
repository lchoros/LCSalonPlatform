using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NodaTime;

using Salon.Core.Appointments;
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

        // A card merged into another (ADR 0017) is a pointer, not a client the desk should pick.
        var clients = db.Clients.AsNoTracking().Where(c => c.MergedIntoClientId == null);
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
            client.UpdatedAt,
            client.MergedIntoClientId,
            client.MergedAt);
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

    public async Task UpdateDetailsAsync(Guid clientId, string firstName, string lastName, string? phone, string? email, string? notes, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Updating a client requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);

        if (!ClientName.TryNormalise(firstName, out var first))
        {
            throw new ArgumentException($"First name is required and can have at most {Client.NameMaxLength} characters.", nameof(firstName));
        }

        if (!ClientName.TryNormalise(lastName, out var last))
        {
            throw new ArgumentException($"Last name is required and can have at most {Client.NameMaxLength} characters.", nameof(lastName));
        }

        if (!PhoneNumber.TryNormalise(phone, tenant?.Currency, out var normalisedPhone))
        {
            throw new ArgumentException("The phone number is not valid; use the country code for numbers from abroad.", nameof(phone));
        }

        if (!EmailAddress.TryNormalise(email, out var normalisedEmail))
        {
            throw new ArgumentException($"The email must look like name@domain and have at most {Client.EmailMaxLength} characters.", nameof(email));
        }

        var plainNotes = OptionalText(notes, Client.NotesMaxLength, "Notes");
        var client = await RequireAsync(clientId, cancellationToken).ConfigureAwait(false);

        if (normalisedPhone is not null && await db.Clients.AnyAsync(c => c.Phone == normalisedPhone && c.Id != clientId, cancellationToken).ConfigureAwait(false))
        {
            throw new DuplicateClientException(nameof(Client.Phone), normalisedPhone);
        }

        if (normalisedEmail is not null && await db.Clients.AnyAsync(c => c.Email == normalisedEmail && c.Id != clientId, cancellationToken).ConfigureAwait(false))
        {
            throw new DuplicateClientException(nameof(Client.Email), normalisedEmail);
        }

        client.FirstName = first!;
        client.LastName = last!;
        client.Phone = normalisedPhone;
        client.Email = normalisedEmail;
        client.Notes = plainNotes;
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

    public async Task<ClientHistory?> GetHistoryAsync(Guid clientId, int take = 20, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, MaxSearchTake);

        if (!await db.Clients.AsNoTracking().AnyAsync(c => c.Id == clientId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Reading a client's history requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var zone = (tenant is null ? null : DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId)) ?? DateTimeZone.Utc;
        var now = clock.GetCurrentInstant();

        var visits = db.Appointments.AsNoTracking().Where(a => a.ClientId == clientId);
        var upcoming = await ProjectVisitsAsync(
            visits.Where(a => a.Start >= now && (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Arrived))
                .OrderBy(a => a.Start),
            take, zone, cancellationToken).ConfigureAwait(false);
        var past = await ProjectVisitsAsync(
            visits.Where(a => a.Start < now || (a.Status != AppointmentStatus.Booked && a.Status != AppointmentStatus.Confirmed && a.Status != AppointmentStatus.Arrived))
                .OrderByDescending(a => a.Start),
            take, zone, cancellationToken).ConfigureAwait(false);

        var counts = await visits
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var lastCompleted = await visits
            .Where(a => a.Status == AppointmentStatus.Completed)
            .Select(a => (Instant?)a.Start)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ClientHistory(
            upcoming,
            past,
            counts.Sum(c => c.Count),
            counts.Where(c => c.Status == AppointmentStatus.Completed).Sum(c => c.Count),
            counts.Where(c => c.Status == AppointmentStatus.NoShow).Sum(c => c.Count),
            lastCompleted?.InZone(zone).Date);
    }

    public async Task MergeAsync(Guid targetId, Guid sourceId, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Merging clients requires a tenant in scope.");
        var target = await RequireAsync(targetId, cancellationToken).ConfigureAwait(false);
        var source = targetId == sourceId ? target : await RequireAsync(sourceId, cancellationToken).ConfigureAwait(false);
        if (ClientMergeRules.Refusal(target, source) is { } refusal)
        {
            throw new ClientMergeRefusedException(refusal);
        }

        // Joins the command's transaction when there is one (ADR 0003); otherwise the writes below share their own.
        var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;
        try
        {
            var now = clock.GetCurrentInstant();
            var sourcePhone = source.Phone;
            var sourceEmail = source.Email;

            // The source goes first: phone and email are unique per salon, so they must be free before the target may take them.
            source.Phone = null;
            source.Email = null;
            source.IsActive = false;
            source.MergedIntoClientId = target.Id;
            source.MergedAt = now;
            source.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            target.Notes = ClientMergeRules.UnionNotes(target.Notes, source.Notes);
            target.HealthNotesProtected = ClientMergeRules.ChooseHealthNotes(target.HealthNotesProtected, source.HealthNotesProtected);
            target.LastPatchTestOn = ClientMergeRules.ChoosePatchTest(target.LastPatchTestOn, source.LastPatchTestOn);
            target.Phone = ClientMergeRules.ChooseContact(target.Phone, sourcePhone);
            target.Email = ClientMergeRules.ChooseContact(target.Email, sourceEmail);
            var consent = ClientMergeRules.ChooseConsent(
                new ConsentState(target.MarketingConsent, target.MarketingConsentAt, target.MarketingConsentWording),
                new ConsentState(source.MarketingConsent, source.MarketingConsentAt, source.MarketingConsentWording));
            target.MarketingConsent = consent.Granted;
            target.MarketingConsentAt = consent.At;
            target.MarketingConsentWording = consent.Wording;
            target.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Status-change rows hang off the appointment, so they follow it.
            var moved = await db.Appointments
                .Where(a => a.ClientId == source.Id)
                .ExecuteUpdateAsync(a => a.SetProperty(x => x.ClientId, target.Id), cancellationToken)
                .ConfigureAwait(false);

            // What the source owes or has settled follows it too (ADR 0022): the only write that ever touches an account line.
            await db.ClientAccountEntries
                .Where(e => e.ClientId == source.Id)
                .ExecuteUpdateAsync(e => e.SetProperty(x => x.ClientId, target.Id), cancellationToken)
                .ConfigureAwait(false);

            await publisher.PublishAsync(new ClientsMerged(target.Id, source.Id, tenantId), cancellationToken).ConfigureAwait(false);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            // Ids only: who merged what, never names or contact details.
            logger.LogInformation("Client {SourceId} merged into {TargetId} by user {UserId}; {Count} appointments moved", source.Id, target.Id, byUserId, moved);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<IReadOnlyList<ClientVisit>> ProjectVisitsAsync(IQueryable<Appointment> ordered, int take, DateTimeZone zone, CancellationToken cancellationToken)
    {
        // No navigations on Appointment (ADR 0012): two joins through the tenant filter resolve the names.
        var rows = await ordered
            .Take(take)
            .Join(db.Services.AsNoTracking(), a => a.ServiceId, s => s.Id, (a, s) => new { Appointment = a, ServiceName = s.Name })
            .Join(db.StaffMembers.AsNoTracking(), x => x.Appointment.StaffMemberId, m => m.Id, (x, m) => new
            {
                x.Appointment.Id,
                x.Appointment.Start,
                x.Appointment.DurationMinutes,
                x.Appointment.Status,
                x.Appointment.PriceAmount,
                x.Appointment.Currency,
                x.ServiceName,
                StaffName = m.DisplayName,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(r => new ClientVisit(r.Id, r.Start.InZone(zone), r.DurationMinutes, r.ServiceName, r.StaffName, r.Status, r.PriceAmount, r.Currency)).ToArray();
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