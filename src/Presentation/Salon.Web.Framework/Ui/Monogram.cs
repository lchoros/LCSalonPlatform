namespace Salon.Web.Framework.Ui;

/// <summary>
/// The letter a service circle shows until real photos exist (ADR 0080): price lists name services like
/// "HAIR KIDS 21: Boy's Haircut", so the letter comes from after any code prefix, never from the code.
/// </summary>
public static class Monogram
{
    public static string For(string? title)
    {
        var text = title ?? string.Empty;
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0 && colon + 1 < text.Length)
        {
            text = text[(colon + 1)..];
        }

        text = text.TrimStart();
        return text.Length == 0 ? "·" : char.ToUpperInvariant(text[0]).ToString();
    }
}