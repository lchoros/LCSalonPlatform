using Salon.Core.Clients;

namespace Salon.Services.Clients;

/// <summary>A client's email: optional, trimmed, lower-cased, shaped like <c>name@domain</c> and no longer than the column (ADR 0011).</summary>
public static class EmailAddress
{
    /// <summary>True with a null <paramref name="email"/> for a blank input; false when the text is not an address.</summary>
    public static bool TryNormalise(string? raw, out string? email)
    {
        email = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var normalised = raw.Trim().ToLowerInvariant();
        var at = normalised.IndexOf('@', StringComparison.Ordinal);
        if (normalised.Length > Client.EmailMaxLength
            || at <= 0
            || at == normalised.Length - 1
            || normalised.IndexOf('@', at + 1) >= 0
            || normalised.Any(char.IsWhiteSpace))
        {
            return false;
        }

        email = normalised;
        return true;
    }
}