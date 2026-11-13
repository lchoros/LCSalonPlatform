using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Localization;
using Salon.Core.Messaging;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Caching;

namespace Salon.Services.Messaging;

/// <summary>
/// Reads all of the current tenant's active overrides once into the cache ("Salon.messagetemplates.{tenantId}") and
/// layers them over the embedded defaults; every write upserts or deletes one row and drops the tenant's entry. The EF
/// tenant filter and RLS scope the queries, and the cache key carries the tenant id, so one salon never sees another's text.
/// </summary>
internal sealed class MessageTemplateService(
    SalonDbContext db,
    ITenantContext tenantContext,
    IStaticCacheManager cache,
    EmbeddedMessageTemplateDefaults defaults,
    IClock clock) : IMessageTemplateService
{
    public const string CachePrefix = "Salon.messagetemplates.";

    public async Task<MessageTemplateText> GetAsync(string key, MessageChannel channel, string culture, CancellationToken cancellationToken = default)
    {
        RequireDefinition(key, channel);
        ArgumentNullException.ThrowIfNull(culture);
        var tenantId = RequireTenant();

        var supported = SupportedCultures.Match(culture);
        if (supported is not null)
        {
            var overrides = await GetOverridesAsync(tenantId, cancellationToken).ConfigureAwait(false);
            if (overrides.TryGetValue(OverrideKey(key, channel, supported), out var tenantText))
            {
                return tenantText;
            }

            if (defaults.TryGet(key, channel, supported, out var cultureDefault))
            {
                return cultureDefault;
            }
        }

        return defaults.TryGet(key, channel, SupportedCultures.Default, out var platformDefault)
            ? platformDefault
            : throw new MessageTemplateNotFoundException(key, channel, culture);
    }

    public Task<MessageTemplateText> GetDefaultAsync(string key, MessageChannel channel, string culture, CancellationToken cancellationToken = default)
    {
        RequireDefinition(key, channel);
        ArgumentNullException.ThrowIfNull(culture);
        RequireTenant();

        var supported = SupportedCultures.Match(culture);
        if (supported is not null && defaults.TryGet(key, channel, supported, out var cultureDefault))
        {
            return Task.FromResult(cultureDefault);
        }

        return defaults.TryGet(key, channel, SupportedCultures.Default, out var platformDefault)
            ? Task.FromResult(platformDefault)
            : throw new MessageTemplateNotFoundException(key, channel, culture);
    }

    public async Task<RenderedTemplate> RenderAsync(string key, MessageChannel channel, string culture, MessageTokens tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var text = await GetAsync(key, channel, culture, cancellationToken).ConfigureAwait(false);

        var subject = text.Subject is null ? null : MessageTemplateRenderer.Render(text.Subject, tokens);
        var body = MessageTemplateRenderer.Render(text.Body, tokens);
        var missing = (subject?.MissingTokens ?? []).Concat(body.MissingTokens).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return new RenderedTemplate(subject?.Text, body.Text, missing.AsReadOnly());
    }

    public async Task SaveOverrideAsync(string key, MessageChannel channel, string culture, string? subject, string body, CancellationToken cancellationToken = default)
    {
        var definition = RequireDefinition(key, channel);
        var supported = RequireSupportedCulture(culture);
        ValidateTexts(definition, channel, subject, body);
        var tenantId = RequireTenant();

        var row = await FindRowAsync(key, channel, supported, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = new MessageTemplate { TenantId = tenantId, Key = key, Channel = channel, Culture = supported, Body = body };
            db.MessageTemplates.Add(row);
        }

        row.Subject = subject;
        row.Body = body;
        row.IsActive = true;
        row.UpdatedAt = clock.GetCurrentInstant();

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.RemoveAsync(KeyFor(tenantId)).ConfigureAwait(false);
    }

    public async Task RemoveOverrideAsync(string key, MessageChannel channel, string culture, CancellationToken cancellationToken = default)
    {
        RequireDefinition(key, channel);
        var supported = RequireSupportedCulture(culture);
        var tenantId = RequireTenant();

        var row = await FindRowAsync(key, channel, supported, cancellationToken).ConfigureAwait(false);
        if (row is not null)
        {
            db.MessageTemplates.Remove(row);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await cache.RemoveAsync(KeyFor(tenantId)).ConfigureAwait(false);
    }

    internal static CacheKey KeyFor(Guid tenantId) => CacheKey.Create($"{CachePrefix}{tenantId}", CachePrefix);

    private static string OverrideKey(string key, MessageChannel channel, string culture) => $"{key}|{(int)channel}|{culture}";

    private static MessageTemplateDefinition RequireDefinition(string key, MessageChannel channel)
    {
        var definition = MessageTemplateCatalog.Get(key);
        return definition.Supports(channel) ? definition : throw new MessageTemplateNotFoundException(key, channel);
    }

    private static string RequireSupportedCulture(string culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return SupportedCultures.Match(culture)
               ?? throw new ArgumentException($"'{culture}' is not a supported culture; use one of: {string.Join(", ", SupportedCultures.All)}.", nameof(culture));
    }

    private static void ValidateTexts(MessageTemplateDefinition definition, MessageChannel channel, string? subject, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        switch (channel)
        {
            case MessageChannel.Sms when subject is not null:
                throw new ArgumentException("An SMS template has no subject; pass null.", nameof(subject));
            case MessageChannel.Email when string.IsNullOrWhiteSpace(subject):
                throw new ArgumentException("An email template needs a subject.", nameof(subject));
            case MessageChannel.Email when subject.Length > MessageTemplateRules.SubjectMaxLength:
                throw new ArgumentException($"The subject exceeds {MessageTemplateRules.SubjectMaxLength} characters.", nameof(subject));
        }

        if (subject is not null)
        {
            RequireAllowedTokens(definition, subject, nameof(subject));
        }

        RequireAllowedTokens(definition, body, nameof(body));

        if (body.Length > MessageTemplateRules.BodyMaxLength(channel))
        {
            throw new ArgumentException($"The body exceeds {MessageTemplateRules.BodyMaxLength(channel)} characters.", nameof(body));
        }

        var missing = MessageTemplateRules.MissingRequired(definition, body);
        if (missing.Count > 0)
        {
            throw new ArgumentException($"Template '{definition.Key}' must keep the token(s) {string.Join(", ", missing)}.", nameof(body));
        }
    }

    private static void RequireAllowedTokens(MessageTemplateDefinition definition, string text, string parameterName)
    {
        var disallowed = EmbeddedMessageTemplateDefaults.TokensOf(text).Where(t => !definition.AllowsToken(t)).Order(StringComparer.Ordinal).ToList();
        if (disallowed.Count > 0)
        {
            throw new ArgumentException(
                $"Template '{definition.Key}' does not allow the token(s) {string.Join(", ", disallowed.Select(t => $"{{{{{t}}}}}"))}; allowed: {string.Join(", ", definition.TokenNames)}.",
                parameterName);
        }
    }

    private Guid RequireTenant()
        => tenantContext.TenantId
           ?? throw new TenantViolationException("Message templates are per tenant, but no tenant is set in the current scope.");

    private Task<IReadOnlyDictionary<string, MessageTemplateText>> GetOverridesAsync(Guid tenantId, CancellationToken cancellationToken)
        => cache.GetAsync<IReadOnlyDictionary<string, MessageTemplateText>>(KeyFor(tenantId), async () =>
        {
            var rows = await db.MessageTemplates.AsNoTracking()
                .Where(t => t.IsActive)
                .Select(t => new { t.Key, t.Channel, t.Culture, t.Subject, t.Body })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return rows.ToDictionary(
                r => OverrideKey(r.Key, r.Channel, SupportedCultures.Match(r.Culture) ?? r.Culture),
                r => new MessageTemplateText(r.Subject, r.Body, IsTenantOverride: true),
                StringComparer.Ordinal);
        });

    private Task<MessageTemplate?> FindRowAsync(string key, MessageChannel channel, string culture, CancellationToken cancellationToken)
        => db.MessageTemplates.FirstOrDefaultAsync(t => t.Key == key && t.Channel == channel && t.Culture == culture, cancellationToken);
}