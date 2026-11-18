using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Services.Locations;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Services.Privacy;

/// <summary>The salon's privacy notice as a client reads it (ADR 0063).</summary>
/// <param name="SalonName">Who decides how the details are used.</param>
/// <param name="Address">Where the salon is, on one line; null until the owner enters it.</param>
/// <param name="Phone">How to reach the salon; null when not entered.</param>
/// <param name="Email">How to write to the salon; null when not entered.</param>
/// <param name="OwnParagraphs">The salon's own text as paragraphs; empty when the standard notice is shown.</param>
/// <param name="UpdatedOn">The day the notice last changed.</param>
public sealed record PrivacyNotice(string SalonName, string? Address, string? Phone, string? Email, IReadOnlyList<string> OwnParagraphs, LocalDate UpdatedOn)
{
    /// <summary>True when the salon wrote its own notice; false for the standard one.</summary>
    public bool IsOwnText => OwnParagraphs.Count > 0;

    /// <summary>True when the notice can say how to reach the salon.</summary>
    public bool HasContact => Phone is not null || Email is not null;
}

/// <summary>What the settings page shows and saves.</summary>
public sealed record PrivacyNoticeChoice(bool UseOwnText, string? OwnText);

public enum PrivacyNoticeOutcome
{
    Saved = 1,

    /// <summary>"Use my own text" was chosen and the text is empty.</summary>
    TextRequired = 2,

    /// <summary>The text is longer than <see cref="PrivacyNoticeRules.OwnTextMaxLength"/>.</summary>
    TooLong = 3,
}

/// <summary>
/// The privacy notice of a salon (ADR 0063): what clients are told about their details, where they give them. The
/// standard notice is built from the salon's own name and address and from what this system does; the owner may
/// replace it with their own text.
/// </summary>
public interface IPrivacyNoticeService
{
    /// <summary>The notice as clients read it.</summary>
    Task<PrivacyNotice> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>What the owner chose; the text is kept even while the standard notice is shown.</summary>
    Task<PrivacyNoticeChoice> GetChoiceAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the owner's choice and moves "last updated" to today.</summary>
    Task<PrivacyNoticeOutcome> SaveAsync(PrivacyNoticeChoice choice, CancellationToken cancellationToken = default);
}

internal sealed class PrivacyNoticeService(
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    ILocationService locations,
    IClock clock,
    ILogger<PrivacyNoticeService> logger) : IPrivacyNoticeService
{
    public async Task<PrivacyNotice> GetAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await TenantAsync(cancellationToken).ConfigureAwait(false);
        var notice = await settings.LoadAsync<PrivacyNoticeSettings>(cancellationToken).ConfigureAwait(false);
        var location = await locations.GetMainAsync(cancellationToken).ConfigureAwait(false);
        return new PrivacyNotice(
            tenant.Name,
            location?.Address,
            location?.Phone,
            location?.Email,
            notice.UseOwnText ? PrivacyNoticeRules.Paragraphs(notice.OwnText) : [],
            PrivacyNoticeRules.UpdatedOn(notice));
    }

    public async Task<PrivacyNoticeChoice> GetChoiceAsync(CancellationToken cancellationToken = default)
    {
        await TenantAsync(cancellationToken).ConfigureAwait(false);
        var notice = await settings.LoadAsync<PrivacyNoticeSettings>(cancellationToken).ConfigureAwait(false);
        return new PrivacyNoticeChoice(notice.UseOwnText, notice.OwnText);
    }

    public async Task<PrivacyNoticeOutcome> SaveAsync(PrivacyNoticeChoice choice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choice);
        var tenant = await TenantAsync(cancellationToken).ConfigureAwait(false);
        var text = string.IsNullOrWhiteSpace(choice.OwnText) ? null : choice.OwnText.Trim();
        if (text is { Length: > PrivacyNoticeRules.OwnTextMaxLength })
        {
            return PrivacyNoticeOutcome.TooLong;
        }

        if (choice.UseOwnText && text is null)
        {
            return PrivacyNoticeOutcome.TextRequired;
        }

        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        await settings.SaveAsync(
            new PrivacyNoticeSettings
            {
                UseOwnText = choice.UseOwnText,
                OwnText = text,
                UpdatedOn = LocalDatePattern.Iso.Format(clock.GetCurrentInstant().InZone(zone).Date),
            },
            cancellationToken).ConfigureAwait(false);

        // Which notice the salon shows, never its text.
        logger.LogInformation("Privacy notice of tenant {TenantId} saved: own text {UseOwnText}", tenant.Id, choice.UseOwnText);
        return PrivacyNoticeOutcome.Saved;
    }

    private async Task<TenantInfo> TenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("A privacy notice belongs to a salon, but no tenant is set in the current scope.");
        return await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
    }
}