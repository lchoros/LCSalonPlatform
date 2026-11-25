namespace Salon.Mobile.Core.Api;

/// <summary>The words in <c>ApiProblem.Code</c> (docs/mobile/BOOKING-API.md), and the app's own for what it could not read.</summary>
public static class ApiProblemCodes
{
    public const string Validation = "validation";
    public const string NotFound = "notFound";
    public const string SlotTaken = "slotTaken";
    public const string TooManyCodes = "tooManyCodes";

    /// <summary>Never sent by the server: an answer the app could not read, or a status the doc does not name.</summary>
    public const string Unexpected = "unexpected";

    /// <summary>The code a status stands for when a refusal came without a readable body (a proxy's 404, a bare 429).</summary>
    public static string ForStatus(int statusCode)
    {
        return statusCode switch
        {
            400 => Validation,
            404 => NotFound,
            409 => SlotTaken,
            429 => TooManyCodes,
            _ => Unexpected,
        };
    }
}