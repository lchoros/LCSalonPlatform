namespace Salon.Web.Framework.Api;

/// <summary>
/// The stable words of <see cref="Salon.Api.Contracts.Booking.ApiProblem.Code"/> (docs/mobile/BOOKING-API.md): what the
/// app acts on, whatever the language of the sentence beside it.
/// </summary>
public static class ApiProblemCodes
{
    /// <summary>400: something the client can put right; the messages are by field.</summary>
    public const string Validation = "validation";

    /// <summary>404: nothing of this salon goes by that id.</summary>
    public const string NotFound = "notFound";

    /// <summary>409: the time has just been taken.</summary>
    public const string SlotTaken = "slotTaken";

    /// <summary>429: too many codes to one number, or too many tries from one address.</summary>
    public const string TooManyCodes = "tooManyCodes";
}