using System.Text.RegularExpressions;

namespace Salon.Core.Messaging;

/// <summary>
/// Replaces <c>{{Name}}</c> placeholders in a message template with token values (ADR 0008). Names are matched
/// case-insensitively, <c>\{{Name}}</c> renders the placeholder literally, unknown tokens render empty and are
/// reported, and anything that is not a well-formed placeholder is left untouched. Values are inserted verbatim.
/// </summary>
public static partial class MessageTemplateRenderer
{
    /// <summary>
    /// What a placeholder looks like. Group "escape" is the backslash before an escaped placeholder; group "name" is the
    /// token name of a real one. The messages page hands the same pattern to its script (ADR 0098), which reads it with
    /// the same meaning, so the browser's preview finds exactly the placeholders this class does.
    /// </summary>
    public const string PlaceholderPattern = @"(?<escape>\\)?\{\{\s*(?<name>[A-Za-z][A-Za-z0-9_.]{0,63})\s*\}\}";

    internal static Regex PlaceholderRegex => Placeholder();

    [GeneratedRegex(PlaceholderPattern, RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex Placeholder();

    /// <summary>Renders <paramref name="template"/> with <paramref name="tokens"/>; one pass, values are never re-expanded.</summary>
    public static RenderedMessage Render(string template, MessageTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(tokens);

        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var text = Placeholder().Replace(template, match =>
        {
            if (match.Groups["escape"].Success)
            {
                return match.Value[1..];
            }

            var name = match.Groups["name"].Value;
            if (tokens.TryGet(name, out var value))
            {
                return value;
            }

            if (seen.Add(name))
            {
                missing.Add(name);
            }

            return string.Empty;
        });

        return new RenderedMessage(text, missing.AsReadOnly());
    }
}