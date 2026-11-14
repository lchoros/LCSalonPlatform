using FluentValidation;
using Salon.Core.Cqrs;
using Salon.Core.Messaging;
using Salon.Services.Localization;

namespace Salon.Web.Features.Settings.MessageTemplates;

/// <summary>Every message the salon sends to clients, per channel, with whether the salon wrote its own wording (ADR 0030).</summary>
public sealed record ListMessageTemplatesQuery : IQuery<IReadOnlyList<MessageTemplateRow>>;

public sealed record MessageTemplateRow(string Key, MessageChannel Channel, bool IsOwn);

/// <summary>One message on one channel as the edit page shows it; null when the catalogue has no such message.</summary>
public sealed record GetMessageTemplateQuery(string Key, MessageChannel Channel) : IQuery<MessageTemplateDetails?>;

/// <param name="Subject">The wording in use; null for a text.</param>
/// <param name="IsOwn">True when the salon saved its own wording; false while the standard one applies.</param>
/// <param name="Placeholders">Names the message may use, in catalogue order.</param>
/// <param name="Required">Names the message must keep.</param>
public sealed record MessageTemplateDetails(
    string Key,
    MessageChannel Channel,
    string? Subject,
    string Body,
    bool IsOwn,
    string? StandardSubject,
    string StandardBody,
    IReadOnlyList<string> Placeholders,
    IReadOnlyList<string> Required);

/// <summary>What a client would receive for <paramref name="Subject"/> and <paramref name="Body"/>, filled with made-up details. Nothing is saved.</summary>
public sealed record PreviewMessageTemplateQuery(string Key, MessageChannel Channel, string? Subject, string? Body) : IQuery<MessagePreview>;

/// <param name="Length">How many texts the example costs; null for an email.</param>
public sealed record MessagePreview(string? Subject, string Body, SmsLength? Length);

/// <summary>Saves the salon's own wording of one message on one channel, in the salon's language.</summary>
public sealed record SaveMessageTemplateCommand(string Key, MessageChannel Channel, string? Subject, string? Body) : ICommand<bool>;

/// <summary>Deletes the salon's own wording, so the standard one applies again.</summary>
public sealed record ResetMessageTemplateCommand(string Key, MessageChannel Channel) : ICommand<bool>;

public sealed class SaveMessageTemplateValidator : AbstractValidator<SaveMessageTemplateCommand>
{
    public SaveMessageTemplateValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        // An unknown message never gets here: the controller answers 404 first. The guard keeps the rules below simple.
        When(c => MessageTemplateCatalog.TryGet(c.Key ?? string.Empty, out var definition) && definition.Supports(c.Channel), () =>
        {
            RuleFor(c => c.Body)
                .Cascade(CascadeMode.Stop)
                .Must(body => !string.IsNullOrWhiteSpace(body))
                .WithMessage(_ => localizer["Settings.Messages.Validation.BodyRequired"])
                .Must((c, body) => body!.Length <= MessageTemplateRules.BodyMaxLength(c.Channel))
                .WithMessage(c => localizer.Format("Settings.Messages.Validation.TooLong", MessageTemplateRules.BodyMaxLength(c.Channel)))
                .Must((c, body) => MessageTemplateRules.NotAllowed(MessageTemplateCatalog.Get(c.Key), body).Count == 0)
                .WithMessage(c => NotAllowed(localizer, c.Key, c.Body))
                .Must((c, body) => MessageTemplateRules.MissingRequired(MessageTemplateCatalog.Get(c.Key), body).Count == 0)
                .WithMessage(c => localizer.Format("Settings.Messages.Validation.Required", Braces(MessageTemplateRules.MissingRequired(MessageTemplateCatalog.Get(c.Key), c.Body))));

            When(c => c.Channel == MessageChannel.Email, () =>
            {
                RuleFor(c => c.Subject)
                    .Cascade(CascadeMode.Stop)
                    .Must(subject => !string.IsNullOrWhiteSpace(subject))
                    .WithMessage(_ => localizer["Settings.Messages.Validation.SubjectRequired"])
                    .Must(subject => subject!.Trim().Length <= MessageTemplateRules.SubjectMaxLength)
                    .WithMessage(_ => localizer.Format("Settings.Messages.Validation.TooLong", MessageTemplateRules.SubjectMaxLength))
                    .Must((c, subject) => MessageTemplateRules.NotAllowed(MessageTemplateCatalog.Get(c.Key), subject).Count == 0)
                    .WithMessage(c => NotAllowed(localizer, c.Key, c.Subject));
            });
        });
    }

    /// <summary>Names written the way the owner types them: <c>{{Name}}</c>, separated by commas.</summary>
    public static string Braces(IEnumerable<string> names) => string.Join(", ", names.Select(name => "{{" + name + "}}"));

    private static string NotAllowed(ILocalizationService localizer, string key, string? text)
    {
        var definition = MessageTemplateCatalog.Get(key);
        return localizer.Format("Settings.Messages.Validation.NotAllowed", Braces(MessageTemplateRules.NotAllowed(definition, text)), Braces(definition.TokenNames));
    }
}