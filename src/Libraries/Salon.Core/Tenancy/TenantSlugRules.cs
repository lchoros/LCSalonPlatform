namespace Salon.Core.Tenancy;

/// <summary>
/// What a salon's web address may be (ADR 0075): the slug becomes "{slug}.{base domain}", so it must be a clean DNS
/// label, and names the platform uses for itself are kept back. Pure rules, shared by sign-up and whatever renames
/// a salon later.
/// </summary>
public static class TenantSlugRules
{
    public const int MinLength = 3;

    /// <summary>Shorter than the DNS label cap (63), so the full host never grows awkward.</summary>
    public const int MaxLength = 40;

    /// <summary>Names the platform keeps for itself; a salon on "www" or "api" would shadow them.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "www", "api", "app", "admin", "mail", "smtp", "imap", "demo", "help", "support", "book", "booking",
        "blog", "status", "staging", "test", "dev", "my", "signup", "sign-up", "account", "accounts",
        "assets", "static", "cdn", "docs", "salon", "ftp", "ns1", "ns2",
    };

    /// <summary>Trimmed and lower-case: "Studio-20 " and "studio-20" are one address.</summary>
    public static string Normalize(string? slug) => (slug ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Letters a-z, digits and single hyphens between them, 3 to 40 characters.</summary>
    public static bool IsWellFormed(string slug)
    {
        ArgumentNullException.ThrowIfNull(slug);
        if (slug.Length is < MinLength or > MaxLength || slug[0] == '-' || slug[^1] == '-' || slug.Contains("--", StringComparison.Ordinal))
        {
            return false;
        }

        return slug.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
    }

    public static bool IsReserved(string slug) => Reserved.Contains(slug);
}