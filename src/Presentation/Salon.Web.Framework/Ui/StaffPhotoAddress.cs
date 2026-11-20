namespace Salon.Web.Framework.Ui;

/// <summary>
/// Where a staff member's picture is fetched from (ADR 0088). The ShowPhoto slice owns the route; it is named here
/// so the slices that show a face can write its address without referencing that slice.
/// </summary>
public static class StaffPhotoAddress
{
    /// <summary>The route template of the picture, on the salon's own host.</summary>
    public const string Route = Folder + "/{staffMemberId:guid}";

    private const string Folder = "staff-photo";

    /// <summary>The query value that names the version; a browser keeps what it fetched under one version for good.</summary>
    public const string VersionParameter = "v";

    /// <summary>The address of the person's picture in that version, or null when they have none (a letter is shown instead).</summary>
    public static string? For(Guid staffMemberId, string? version)
        => string.IsNullOrEmpty(version) ? null : $"/{Folder}/{staffMemberId}?{VersionParameter}={Uri.EscapeDataString(version)}";

    /// <summary>
    /// How long a browser may keep what the address gave it (ADR 0088). Asked with the current version, the picture is
    /// kept for a year and never asked for again, because the address changes when the picture does; without it the
    /// browser must ask before using its copy. A signed-in user's copy stays in their own browser: an answer to a
    /// request that carried the sign-in cookie can carry a renewed one, which a cache others share must never keep.
    /// Somebody who no longer works here is shown to signed-in users only, and asked for each time.
    /// </summary>
    public static string CacheControlFor(bool staffIsActive, bool isCurrentVersion, bool signedIn)
    {
        if (!staffIsActive)
        {
            return "private, no-cache";
        }

        if (!isCurrentVersion)
        {
            return signedIn ? "private, no-cache" : "no-cache";
        }

        return signedIn ? "private, max-age=31536000, immutable" : "public, max-age=31536000, immutable";
    }

    /// <summary>The address of the person's picture when the list of versions has one for them, else null.</summary>
    public static string? For(Guid staffMemberId, IReadOnlyDictionary<Guid, string> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        return versions.TryGetValue(staffMemberId, out var version) ? For(staffMemberId, version) : null;
    }
}