using Salon.Api.Contracts.Booking;

namespace Salon.Mobile.Core.Api;

/// <summary>The six calls of /api/v1/booking (docs/mobile/BOOKING-API.md), one method each.</summary>
public interface IBookingApi
{
    /// <summary>GET /services: the salon and every active service.</summary>
    Task<ApiResult<ServiceList>> GetServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>POST /availability: the time screen for the visit as chosen so far.</summary>
    Task<ApiResult<Availability>> GetAvailabilityAsync(AvailabilityRequest request, CancellationToken cancellationToken = default);

    /// <summary>GET /terms: what the details screen says before the client commits.</summary>
    Task<ApiResult<BookingTerms>> GetTermsAsync(CancellationToken cancellationToken = default);

    /// <summary>POST /start: the visit and the client's details; a code is sent, or the visit is booked.</summary>
    Task<ApiResult<StartBookingResponse>> StartAsync(StartBookingRequest request, CancellationToken cancellationToken = default);

    /// <summary>POST /verify/{id}: the code from the text.</summary>
    Task<ApiResult<VerifyResponse>> VerifyAsync(Guid verificationId, VerifyRequest request, CancellationToken cancellationToken = default);

    /// <summary>GET /visits/{id}: the confirmation of a booked visit.</summary>
    Task<ApiResult<BookedVisit>> GetVisitAsync(Guid appointmentId, CancellationToken cancellationToken = default);
}