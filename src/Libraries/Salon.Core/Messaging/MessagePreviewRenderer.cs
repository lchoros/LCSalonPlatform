namespace Salon.Core.Messaging;

/// <summary>What one piece of a previewed message is.</summary>
public enum PreviewPartKind
{
    /// <summary>Words the salon typed, sent as they are.</summary>
    Text,

    /// <summary>A placeholder the message offers, filled with its example value.</summary>
    Value,

    /// <summary>A placeholder the message does not offer (misspelt, or from another message): saving refuses it.</summary>
    Unknown,
}

/// <param name="Text">What the piece shows: the words, the example value, or the placeholder as typed.</param>
public readonly record struct PreviewPart(PreviewPartKind Kind, string Text);

/// <summary>A message as the preview shows it, in pieces, so the page can colour the filled-in values and mark the unknown ones.</summary>
public sealed record PreviewedText(IReadOnlyList<PreviewPart> Parts)
{
    /// <summary>What a client would receive: an unknown placeholder comes out empty, as it does in a real message.</summary>
    public string Sent => string.Concat(Parts.Where(p => p.Kind != PreviewPartKind.Unknown).Select(p => p.Text));

    public bool HasUnknown => Parts.Any(p => p.Kind == PreviewPartKind.Unknown);
}

/// <summary>
/// Turns a template into the pieces of its preview (ADR 0098). Pure. A placeholder is what
/// <see cref="MessageTemplateRenderer"/> treats as one; it is known exactly when the message's catalogue entry allows it
/// (<see cref="MessageTemplateRules.NotAllowed"/>), so what the preview marks is what saving refuses. An escaped
/// placeholder (<c>\{{Name}}</c>) is text.
/// </summary>
public static class MessagePreviewRenderer
{
    public static PreviewedText Render(MessageTemplateDefinition definition, string? template, MessageTokens samples)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(samples);

        var parts = new List<PreviewPart>();
        if (string.IsNullOrEmpty(template))
        {
            return new PreviewedText(parts);
        }

        var at = 0;
        foreach (var match in MessageTemplateRenderer.PlaceholderRegex.EnumerateMatches(template))
        {
            AddText(parts, template[at..match.Index]);
            var placeholder = template.Substring(match.Index, match.Length);
            at = match.Index + match.Length;

            if (placeholder[0] == '\\')
            {
                AddText(parts, placeholder[1..]);
                continue;
            }

            var name = placeholder[2..^2].Trim();
            parts.Add(definition.AllowsToken(name)
                ? new PreviewPart(PreviewPartKind.Value, samples.TryGet(name, out var value) ? value : string.Empty)
                : new PreviewPart(PreviewPartKind.Unknown, placeholder));
        }

        AddText(parts, template[at..]);
        return new PreviewedText(parts);
    }

    /// <summary>Neighbouring words become one piece, so the page writes them as one run of text.</summary>
    private static void AddText(List<PreviewPart> parts, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (parts.Count > 0 && parts[^1].Kind == PreviewPartKind.Text)
        {
            parts[^1] = parts[^1] with { Text = parts[^1].Text + text };
            return;
        }

        parts.Add(new PreviewPart(PreviewPartKind.Text, text));
    }
}