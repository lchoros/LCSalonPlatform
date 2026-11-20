namespace Salon.Web.Models;

/// <summary>What the friendly status page says (ADR 0087): one of four voices, chosen from the HTTP status.</summary>
public enum StatusKind
{
    NotFound = 1,
    Forbidden = 2,
    TooMany = 3,
    Other = 4,
}

/// <param name="SignedIn">Staff get a way back to the diary; a client gets the salon's front page.</param>
public sealed record StatusViewModel(StatusKind Kind, bool SignedIn)
{
    public static StatusKind KindOf(int statusCode) => statusCode switch
    {
        404 or 410 => StatusKind.NotFound,
        401 or 403 => StatusKind.Forbidden,
        429 => StatusKind.TooMany,
        _ => StatusKind.Other,
    };

    /// <summary>The resource key stem: <c>Error.NotFound</c>, <c>Error.Forbidden</c>, <c>Error.TooMany</c> or <c>Error.Other</c>.</summary>
    public string KeyStem => Kind switch
    {
        StatusKind.NotFound => "Error.NotFound",
        StatusKind.Forbidden => "Error.Forbidden",
        StatusKind.TooMany => "Error.TooMany",
        _ => "Error.Other",
    };
}