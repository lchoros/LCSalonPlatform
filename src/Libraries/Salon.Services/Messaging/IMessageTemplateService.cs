using Salon.Core.Messaging;

namespace Salon.Services.Messaging;

/// <summary>
/// Message templates per tenant (ADR 0008). Text resolves as: the tenant's active override for (key, channel, culture),
/// else the embedded platform default for that culture, else the default for <see cref="Salon.Core.Localization.SupportedCultures.Default"/>.
/// Keys come from <see cref="MessageTemplateCatalog"/>; an unknown key throws <see cref="MessageTemplateNotFoundException"/>
/// before any lookup. Every member throws <see cref="Salon.Core.Tenancy.TenantViolationException"/> when no tenant is in scope.
/// </summary>
public interface IMessageTemplateService
{
    /// <summary>The unrendered text for a template, with placeholders in place.</summary>
    Task<MessageTemplateText> GetAsync(string key, MessageChannel channel, string culture, CancellationToken cancellationToken = default);

    /// <summary>The platform's standard wording, whatever the tenant has saved: what "use the standard text again" goes back to.</summary>
    Task<MessageTemplateText> GetDefaultAsync(string key, MessageChannel channel, string culture, CancellationToken cancellationToken = default);

    /// <summary>Resolves the text like <see cref="GetAsync"/> and renders subject and body with <paramref name="tokens"/>.</summary>
    Task<RenderedTemplate> RenderAsync(string key, MessageChannel channel, string culture, MessageTokens tokens, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces the tenant's override. <paramref name="culture"/> must be a supported culture, <paramref name="body"/>
    /// non-blank, <paramref name="subject"/> required for email and null for SMS, and every placeholder a catalogue token for
    /// the key; otherwise <see cref="ArgumentException"/>. Invalidates the tenant's template cache.
    /// </summary>
    Task SaveOverrideAsync(string key, MessageChannel channel, string culture, string? subject, string body, CancellationToken cancellationToken = default);

    /// <summary>Deletes the tenant's override if one exists, so the embedded default applies again. Invalidates the cache.</summary>
    Task RemoveOverrideAsync(string key, MessageChannel channel, string culture, CancellationToken cancellationToken = default);
}