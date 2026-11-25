using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Salon.Core.Messaging;

namespace Salon.Web.Features.Settings.MessageTemplates;

/// <summary>
/// How the edit page writes a message's preview (ADR 0098), kept out of the view (ADR 0043): the words as they are, an
/// example value coloured, an unknown placeholder in red with the words "unknown tag". Everything is HTML-encoded.
/// <c>wwwroot/js/message-tags.js</c> builds the same elements in the browser while the person types.
/// </summary>
public static class MessageEditText
{
    public const string ValueClass = "tag-value";
    public const string UnknownClass = "tag-unknown";
    public const string UnknownNoteClass = "tag-unknown-note";

    /// <summary>A placeholder the way it is typed into the box: <c>{{ClientFirstName}}</c>.</summary>
    public static string Placeholder(string name) => "{{" + name + "}}";

    public static IHtmlContent Preview(PreviewedText text, string unknownLabel)
    {
        ArgumentNullException.ThrowIfNull(text);
        var html = new HtmlContentBuilder();
        foreach (var part in text.Parts)
        {
            switch (part.Kind)
            {
                case PreviewPartKind.Value:
                    html.AppendHtml(Mark(ValueClass, part.Text));
                    break;
                case PreviewPartKind.Unknown:
                    var mark = Mark(UnknownClass, part.Text + " ");
                    var note = new TagBuilder("span");
                    note.AddCssClass(UnknownNoteClass);
                    note.InnerHtml.Append(unknownLabel);
                    mark.InnerHtml.AppendHtml(note);
                    html.AppendHtml(mark);
                    break;
                default:
                    html.Append(part.Text);
                    break;
            }
        }

        return html;
    }

    private static TagBuilder Mark(string cssClass, string text)
    {
        var mark = new TagBuilder("mark");
        mark.AddCssClass(cssClass);
        mark.InnerHtml.Append(text);
        return mark;
    }
}