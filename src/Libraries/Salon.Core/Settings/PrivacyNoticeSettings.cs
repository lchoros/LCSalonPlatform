using NodaTime;
using NodaTime.Text;

namespace Salon.Core.Settings;

/// <summary>
/// What the salon tells its clients about their details (ADR 0063). By default the standard notice, which says what
/// this system does with them; the owner may put their own text in its place.
/// </summary>
public sealed class PrivacyNoticeSettings : ISettings
{
    /// <summary>True when the salon's own text is shown in place of the standard notice.</summary>
    public bool UseOwnText { get; set; }

    /// <summary>The salon's own notice, as plain text with a blank line between paragraphs.</summary>
    public string? OwnText { get; set; }

    /// <summary>The day the owner last saved the notice (ISO date, the salon's day); null until they do.</summary>
    public string? UpdatedOn { get; set; }
}

/// <summary>The rules of the privacy notice (ADR 0063). Pure.</summary>
public static class PrivacyNoticeRules
{
    /// <summary>Room for a notice a lawyer wrote; more than that nobody reads on a phone.</summary>
    public const int OwnTextMaxLength = 20_000;

    /// <summary>
    /// The day the standard notice's wording was last changed. Shown as "last updated" until the owner saves the
    /// page, and moved on by whoever changes the standard text.
    /// </summary>
    public static LocalDate StandardTextDate { get; } = new(2026, 11, 18);

    /// <summary>A salon's own text must say something and fit.</summary>
    public static bool IsOwnText(string? text) => !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= OwnTextMaxLength;

    /// <summary>
    /// The text as paragraphs: split where the writer left a blank line, each one trimmed, empty ones dropped. A
    /// single line break inside a paragraph is kept for the page to show.
    /// </summary>
    public static IReadOnlyList<string> Paragraphs(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return
        [
            .. text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(paragraph => paragraph.Trim('\n', ' ', '\t'))
                .Where(paragraph => paragraph.Length > 0),
        ];
    }

    /// <summary>The day to show as "last updated": the day the owner saved, or the standard text's own day.</summary>
    public static LocalDate UpdatedOn(PrivacyNoticeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.UpdatedOn is { Length: > 0 } saved && LocalDatePattern.Iso.Parse(saved) is { Success: true } parsed
            ? parsed.Value
            : StandardTextDate;
    }
}