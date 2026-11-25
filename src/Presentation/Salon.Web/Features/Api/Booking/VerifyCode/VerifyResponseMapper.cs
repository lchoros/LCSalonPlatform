using Salon.Api.Contracts.Booking;
using Salon.Services.Booking;

namespace Salon.Web.Features.Api.Booking.VerifyCode;

/// <summary>The outcomes of a code as the API's words (<see cref="VerifyOutcomes"/>).</summary>
public static class VerifyResponseMapper
{
    /// <summary>The word for an outcome; null for <see cref="PublicBookingVerificationOutcome.NotFound"/>, which is a 404.</summary>
    public static string? Outcome(PublicBookingVerificationOutcome outcome) => outcome switch
    {
        PublicBookingVerificationOutcome.Booked => VerifyOutcomes.Booked,
        PublicBookingVerificationOutcome.WrongCode => VerifyOutcomes.WrongCode,
        PublicBookingVerificationOutcome.Expired => VerifyOutcomes.Expired,
        PublicBookingVerificationOutcome.TooManyAttempts => VerifyOutcomes.TooManyAttempts,
        PublicBookingVerificationOutcome.SlotTaken => VerifyOutcomes.SlotTaken,
        PublicBookingVerificationOutcome.NotFound => null,
        _ => throw new InvalidOperationException($"Unexpected verification outcome {outcome}."),
    };
}