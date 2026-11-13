using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Salon.Services.Notifications;

/// <summary>
/// Turns the plain text of an email template into the HTML part of the message (ADR 0025). Pure. Templates stay plain
/// text so a salon owner can edit them without knowing HTML: every piece is encoded, line breaks become
/// <c>&lt;br&gt;</c> and web addresses become links. Nothing a template or a token contains can inject markup.
/// </summary>
public static partial class EmailBodies
{
    public static string ToHtml(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var plain = text.ReplaceLineEndings("\n");
        var html = new StringBuilder("<p>");
        var position = 0;
        foreach (Match match in UrlPattern().Matches(plain))
        {
            Append(html, plain[position..match.Index]);
            var url = WebUtility.HtmlEncode(match.Value);
            html.Append("<a href=\"").Append(url).Append("\">").Append(url).Append("</a>");
            position = match.Index + match.Length;
        }

        Append(html, plain[position..]);
        return html.Append("</p>").ToString();
    }

    private static void Append(StringBuilder html, string text)
        => html.Append(WebUtility.HtmlEncode(text).Replace("\n", "<br>\n", StringComparison.Ordinal));

    /// <summary>An http(s) address up to the next space, quote or angle bracket, without the punctuation that ends a sentence.</summary>
    [GeneratedRegex(@"https?://[^\s<>""']*[^\s<>""'.,;:!?)]", RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();
}