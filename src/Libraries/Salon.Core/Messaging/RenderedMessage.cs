namespace Salon.Core.Messaging;

/// <summary>The outcome of rendering a template: the text, and the token names the template used but the caller did not supply.</summary>
/// <param name="Text">The rendered text; unknown tokens render as empty strings.</param>
/// <param name="MissingTokens">Distinct (case-insensitive) unknown token names in order of first appearance.</param>
public sealed record RenderedMessage(string Text, IReadOnlyList<string> MissingTokens)
{
    /// <summary>True when every placeholder in the template had a value.</summary>
    public bool IsComplete => MissingTokens.Count == 0;
}