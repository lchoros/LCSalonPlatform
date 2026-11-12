using Salon.Core.Clients;

namespace Salon.Services.Clients;

/// <summary>A first or last name: trimmed, present, and no longer than the column (ADR 0011).</summary>
public static class ClientName
{
    public static bool TryNormalise(string? raw, out string? name)
    {
        name = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length > Client.NameMaxLength)
        {
            return false;
        }

        name = trimmed;
        return true;
    }
}