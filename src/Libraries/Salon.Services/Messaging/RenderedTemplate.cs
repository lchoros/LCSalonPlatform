namespace Salon.Services.Messaging;

/// <summary>A template rendered with tokens: subject (email only) and body, plus the token names neither text could resolve.</summary>
/// <param name="Subject">Rendered subject; null for SMS.</param>
/// <param name="Body">Rendered body; unknown tokens render as empty strings.</param>
/// <param name="MissingTokens">Distinct (case-insensitive) unknown token names, subject first, each in order of first appearance.</param>
public sealed record RenderedTemplate(string? Subject, string Body, IReadOnlyList<string> MissingTokens)
{
    /// <summary>True when every placeholder in subject and body had a value.</summary>
    public bool IsComplete => MissingTokens.Count == 0;
}