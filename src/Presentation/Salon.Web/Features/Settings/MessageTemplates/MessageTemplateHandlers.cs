using System.Globalization;
using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Messaging;
using Salon.Core.Tenancy;
using Salon.Services.Localization;
using Salon.Services.Messaging;
using Salon.Services.Notifications;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Settings.MessageTemplates;

/// <summary>
/// The salon's language is the language its clients are written to (messages go out in the tenant's culture), so
/// that is the wording these handlers read and write, whatever language the person at the screen has chosen.
/// </summary>
internal static class SalonMessageCulture
{
    public static async Task<TenantInfo> TenantAsync(ITenantContext tenantContext, ITenantLookup tenantLookup, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Message templates are per tenant, but no tenant is set in the current scope.");
        return await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
    }
}

public sealed class ListMessageTemplatesHandler(IMessageTemplateService templates, ITenantContext tenantContext, ITenantLookup tenantLookup)
    : IRequestHandler<ListMessageTemplatesQuery, IReadOnlyList<MessageTemplateRow>>
{
    public async Task<IReadOnlyList<MessageTemplateRow>> HandleAsync(ListMessageTemplatesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = await SalonMessageCulture.TenantAsync(tenantContext, tenantLookup, cancellationToken);

        var rows = new List<MessageTemplateRow>();
        foreach (var definition in MessageTemplateCatalog.All)
        {
            foreach (var channel in definition.Channels)
            {
                var text = await templates.GetAsync(definition.Key, channel, tenant.Culture, cancellationToken);
                rows.Add(new MessageTemplateRow(definition.Key, channel, text.IsTenantOverride));
            }
        }

        return rows;
    }
}

public sealed class GetMessageTemplateHandler(IMessageTemplateService templates, ITenantContext tenantContext, ITenantLookup tenantLookup)
    : IRequestHandler<GetMessageTemplateQuery, MessageTemplateDetails?>
{
    public async Task<MessageTemplateDetails?> HandleAsync(GetMessageTemplateQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!MessageTemplateCatalog.TryGet(request.Key, out var definition) || !definition.Supports(request.Channel))
        {
            return null;
        }

        var tenant = await SalonMessageCulture.TenantAsync(tenantContext, tenantLookup, cancellationToken);
        var current = await templates.GetAsync(request.Key, request.Channel, tenant.Culture, cancellationToken);
        var standard = await templates.GetDefaultAsync(request.Key, request.Channel, tenant.Culture, cancellationToken);
        return new MessageTemplateDetails(
            request.Key,
            request.Channel,
            current.Subject,
            current.Body,
            current.IsTenantOverride,
            standard.Subject,
            standard.Body,
            definition.TokenNames,
            definition.RequiredTokenNames);
    }
}

public sealed class PreviewMessageTemplateHandler(ITenantContext tenantContext, ITenantLookup tenantLookup, IPublicUrlBuilder urls, IClock clock, ILocalizationService localizer)
    : IRequestHandler<PreviewMessageTemplateQuery, MessagePreview>
{
    public async Task<MessagePreview> HandleAsync(PreviewMessageTemplateQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // An unknown key never gets here: the controller answers 404 first.
        var definition = MessageTemplateCatalog.Get(request.Key);
        var tenant = await SalonMessageCulture.TenantAsync(tenantContext, tenantLookup, cancellationToken);
        var samples = MessagePreviewSamples.For(
            tenant.Name,
            new MessageSamplePeople(
                localizer["Settings.Messages.Sample.ClientFirstName"],
                localizer["Settings.Messages.Sample.ServiceName"],
                localizer["Settings.Messages.Sample.StaffName"]),
            clock.GetCurrentInstant(),
            DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc,
            CultureInfo.GetCultureInfo(tenant.Culture),
            urls.CancelBookingUrl(tenant.Slug, MessagePreviewSamples.SampleLinkToken),
            urls.BookingUrl(tenant.Slug));

        // Only what the message offers is filled in; anything else is marked, as saving refuses it (ADR 0098).
        var body = MessagePreviewRenderer.Render(definition, request.Body, samples);
        var subject = request.Channel == MessageChannel.Email ? MessagePreviewRenderer.Render(definition, request.Subject, samples) : null;
        var tags = definition.TokenNames
            .Select(name => new MessageTag(name, samples.TryGet(name, out var value) ? value : string.Empty, definition.RequiredTokenNames.Contains(name)))
            .ToArray();
        return new MessagePreview(subject, body, request.Channel == MessageChannel.Sms ? SmsLengthRules.Measure(body.Sent) : null, tags);
    }
}

public sealed class SaveMessageTemplateHandler(IMessageTemplateService templates, ITenantContext tenantContext, ITenantLookup tenantLookup)
    : IRequestHandler<SaveMessageTemplateCommand, bool>
{
    public async Task<bool> HandleAsync(SaveMessageTemplateCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has run in the pipeline; the service repeats the checks that keep the table sound.
        var tenant = await SalonMessageCulture.TenantAsync(tenantContext, tenantLookup, cancellationToken);
        var subject = request.Channel == MessageChannel.Email ? request.Subject?.Trim() : null;
        var body = (request.Body ?? string.Empty).ReplaceLineEndings("\n").Trim();
        await templates.SaveOverrideAsync(request.Key, request.Channel, tenant.Culture, subject, body, cancellationToken);
        return true;
    }
}

public sealed class ResetMessageTemplateHandler(IMessageTemplateService templates, ITenantContext tenantContext, ITenantLookup tenantLookup)
    : IRequestHandler<ResetMessageTemplateCommand, bool>
{
    public async Task<bool> HandleAsync(ResetMessageTemplateCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = await SalonMessageCulture.TenantAsync(tenantContext, tenantLookup, cancellationToken);
        await templates.RemoveOverrideAsync(request.Key, request.Channel, tenant.Culture, cancellationToken);
        return true;
    }
}