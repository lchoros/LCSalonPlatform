namespace Salon.Core.Messaging;

/// <summary>
/// What a salon's own wording of a message must respect (ADR 0008, ADR 0030). Pure. A text may only use the
/// placeholders its catalogue entry lists, must keep the ones the message is useless without (the code in a
/// verification text), and has a length a phone or a mailbox takes.
/// </summary>
public static class MessageTemplateRules
{
    public const int SubjectMaxLength = 200;

    /// <summary>Three texts' worth before the placeholders are filled in; longer wording belongs in an email.</summary>
    public const int SmsBodyMaxLength = 480;

    public const int EmailBodyMaxLength = 4000;

    public static int BodyMaxLength(MessageChannel channel) => channel == MessageChannel.Sms ? SmsBodyMaxLength : EmailBodyMaxLength;

    /// <summary>
    /// The placeholder names a text uses, each once, in order of first appearance. An escaped placeholder
    /// (<c>\{{Name}}</c>) is text, not a placeholder.
    /// </summary>
    public static IReadOnlyList<string> Placeholders(string? text)
        => string.IsNullOrEmpty(text) ? [] : MessageTemplateRenderer.Render(text, MessageTokens.Empty).MissingTokens;

    /// <summary>Placeholders in <paramref name="text"/> that the message does not offer.</summary>
    public static IReadOnlyList<string> NotAllowed(MessageTemplateDefinition definition, string? text)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Placeholders(text).Where(name => !definition.AllowsToken(name)).ToArray();
    }

    /// <summary>Placeholders the message cannot do without and <paramref name="body"/> does not use.</summary>
    public static IReadOnlyList<string> MissingRequired(MessageTemplateDefinition definition, string? body)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var used = Placeholders(body);
        return definition.RequiredTokenNames.Where(name => !used.Contains(name, StringComparer.OrdinalIgnoreCase)).ToArray();
    }
}