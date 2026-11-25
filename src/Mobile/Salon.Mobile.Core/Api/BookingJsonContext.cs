using System.Text.Json;
using System.Text.Json.Serialization;
using Salon.Api.Contracts.Booking;

namespace Salon.Mobile.Core.Api;

/// <summary>
/// The JSON of the booking API, generated at build time (camelCase, the web defaults), so nothing depends on reflection
/// once the app is trimmed on a phone.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ServiceList))]
[JsonSerializable(typeof(AvailabilityRequest))]
[JsonSerializable(typeof(Availability))]
[JsonSerializable(typeof(BookingTerms))]
[JsonSerializable(typeof(StartBookingRequest))]
[JsonSerializable(typeof(StartBookingResponse))]
[JsonSerializable(typeof(VerifyRequest))]
[JsonSerializable(typeof(VerifyResponse))]
[JsonSerializable(typeof(BookedVisit))]
[JsonSerializable(typeof(ApiProblem))]
internal sealed partial class BookingJsonContext : JsonSerializerContext
{
}