using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Messaging;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Settings.MessageTemplates;

/// <summary>
/// <c>/admin/settings/messages</c> (ADR 0030): the texts and emails clients receive, each with the salon's own wording
/// or the standard one. The list, an edit page per message and channel with a preview filled with made-up details,
/// saving, previewing without saving, and going back to the standard wording. Tenant only, behind
/// <see cref="Permissions.TemplatesManage"/> (Owner and Manager). An unknown message or channel is a 404.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.TemplatesManage)]
[Route("admin/settings/messages")]
public sealed class MessageTemplatesController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string Path = "/admin/settings/messages";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new MessageTemplateListViewModel(await dispatcher.SendAsync(new ListMessageTemplatesQuery(), cancellationToken), TakeFlash()));

    [HttpGet("{key}/{channel}")]
    public async Task<IActionResult> Edit(string key, string channel, CancellationToken cancellationToken)
    {
        if (await FindAsync(key, channel, cancellationToken) is not { } details)
        {
            return NotFound();
        }

        return await PageAsync(details, details.Subject, details.Body, unsaved: false, TakeFlash(), cancellationToken);
    }

    [HttpPost("{key}/{channel}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string key, string channel, string? subject, string? body, CancellationToken cancellationToken)
    {
        if (await FindAsync(key, channel, cancellationToken) is not { } details)
        {
            return NotFound();
        }

        try
        {
            await dispatcher.SendAsync(new SaveMessageTemplateCommand(details.Key, details.Channel, subject, body), cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return await PageAsync(details, subject, body, unsaved: true, null, cancellationToken);
        }

        SetFlash(localizer["Settings.Messages.Saved"]);
        return Redirect(EditPath(details.Key, details.Channel));
    }

    /// <summary>Shows what was typed as a client would receive it. Nothing is saved.</summary>
    [HttpPost("{key}/{channel}/preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(string key, string channel, string? subject, string? body, CancellationToken cancellationToken)
    {
        if (await FindAsync(key, channel, cancellationToken) is not { } details)
        {
            return NotFound();
        }

        return await PageAsync(details, subject, body, unsaved: true, null, cancellationToken);
    }

    [HttpPost("{key}/{channel}/reset")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset(string key, string channel, CancellationToken cancellationToken)
    {
        if (await FindAsync(key, channel, cancellationToken) is not { } details)
        {
            return NotFound();
        }

        await dispatcher.SendAsync(new ResetMessageTemplateCommand(details.Key, details.Channel), cancellationToken);
        SetFlash(localizer["Settings.Messages.ResetDone"]);
        return Redirect(EditPath(details.Key, details.Channel));
    }

    /// <summary>The address of one message on one channel.</summary>
    public static string EditPath(string key, MessageChannel channel) => $"{Path}/{key}/{Slug(channel)}";

    public static string Slug(MessageChannel channel) => channel == MessageChannel.Email ? "email" : "sms";

    private static MessageChannel? ParseChannel(string? slug) => slug?.ToUpperInvariant() switch
    {
        "SMS" => MessageChannel.Sms,
        "EMAIL" => MessageChannel.Email,
        _ => null,
    };

    private async Task<MessageTemplateDetails?> FindAsync(string key, string channel, CancellationToken cancellationToken)
        => ParseChannel(channel) is { } parsed ? await dispatcher.SendAsync(new GetMessageTemplateQuery(key, parsed), cancellationToken) : null;

    private async Task<IActionResult> PageAsync(MessageTemplateDetails details, string? subject, string? body, bool unsaved, string? flash, CancellationToken cancellationToken)
    {
        var preview = await dispatcher.SendAsync(new PreviewMessageTemplateQuery(details.Key, details.Channel, subject, body), cancellationToken);
        return View("Edit", new MessageTemplateEditViewModel(details, subject, body, preview, unsaved, flash));
    }
}

public sealed record MessageTemplateListViewModel(IReadOnlyList<MessageTemplateRow> Rows, string? Flash);

/// <param name="Subject">What the form shows: the saved wording, or what the person typed.</param>
/// <param name="Unsaved">True when the form and the preview show typed text that is not saved.</param>
public sealed record MessageTemplateEditViewModel(MessageTemplateDetails Details, string? Subject, string? Body, MessagePreview Preview, bool Unsaved, string? Flash);