using System.Globalization;
using NodaTime;
using Salon.Core.Booking;
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
        var tenant = await SalonMessageCulture.TenantAsync(tenantContext, tenantLookup, cancellationToken);
        var tokens = Sample(tenant);

        // Rendered the way a real message is: a placeholder the message does not offer comes out empty.
        var body = MessageTemplateRenderer.Render(request.Body ?? string.Empty, tokens).Text;
        var subject = request.Channel == MessageChannel.Email ? MessageTemplateRenderer.Render(request.Subject ?? string.Empty, tokens).Text : null;
        return new MessagePreview(subject, body, request.Channel == MessageChannel.Sms ? SmsLengthRules.Measure(body) : null);
    }

    /// <summary>
    /// Made-up details of realistic length: the salon's own name and links, a visit tomorrow at ten, and a link as long
    /// as a real one, so the count of texts is close to what a client's message will cost.
    /// </summary>
    private MessageTokens Sample(TenantInfo tenant)
    {
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        var tomorrowAtTen = clock.GetCurrentInstant().InZone(zone).Date.PlusDays(1).At(new LocalTime(10, 0)).InZoneLeniently(zone).ToInstant();
        var link = string.Concat(Enumerable.Repeat("example", 7))[..PublicBookingToken.Length];

        return MessageTokens.Empty
            .Add("SalonName", tenant.Name)
            .Add("ClientFirstName", localizer["Settings.Messages.Sample.ClientFirstName"])
            .Add("ServiceName", localizer["Settings.Messages.Sample.ServiceName"])
            .Add("StaffName", localizer["Settings.Messages.Sample.StaffName"])
            .Add("AppointmentStart", BookingSmsTokens.FormatStart(tomorrowAtTen, zone, CultureInfo.GetCultureInfo(tenant.Culture)))
            .Add("CancelUrl", urls.CancelBookingUrl(tenant.Slug, link))
            .Add("BookingUrl", urls.BookingUrl(tenant.Slug))
            .Add("Code", new string('1', PhoneVerificationRules.CodeLength));
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