using Salon.Api.Contracts.Booking;
using Salon.Mobile.Core.Api;

namespace Salon.Tests.Mobile.Fakes;

/// <summary>An <see cref="IBookingApi"/> whose answers a test sets; by default it behaves like <see cref="SampleServer"/>.</summary>
internal sealed class FakeBookingApi : IBookingApi
{
    public FakeBookingApi(SampleServer? server = null)
    {
        Server = server ?? new SampleServer();
        OnServices = () => ApiResult.Ok(Server.ServiceList());
        OnAvailability = (request, _) => Task.FromResult(ApiResult.Ok(Server.Availability(request)));
        OnTerms = () => ApiResult.Ok(Server.Terms);
        OnStart = _ => ApiResult.Ok(new StartBookingResponse(Sample.VerificationId, null, "+44 •••• ••• 789"));
        OnVerify = (_, _) => ApiResult.Ok(new VerifyResponse(VerifyOutcomes.Booked, Sample.AppointmentId, 0));
        OnVisit = _ => ApiResult.Ok(Sample.Visit());
    }

    public SampleServer Server { get; }

    public Func<ApiResult<ServiceList>> OnServices { get; set; }

    public Func<AvailabilityRequest, CancellationToken, Task<ApiResult<Availability>>> OnAvailability { get; set; }

    public Func<ApiResult<BookingTerms>> OnTerms { get; set; }

    public Func<StartBookingRequest, ApiResult<StartBookingResponse>> OnStart { get; set; }

    public Func<Guid, VerifyRequest, ApiResult<VerifyResponse>> OnVerify { get; set; }

    public Func<Guid, ApiResult<BookedVisit>> OnVisit { get; set; }

    public int ServicesCalls { get; private set; }

    public int TermsCalls { get; private set; }

    public List<AvailabilityRequest> AvailabilityRequests { get; } = [];

    public List<StartBookingRequest> StartRequests { get; } = [];

    public List<(Guid VerificationId, VerifyRequest Request)> VerifyRequests { get; } = [];

    public List<Guid> VisitRequests { get; } = [];

    public AvailabilityRequest LastAvailability => AvailabilityRequests[^1];

    public Task<ApiResult<ServiceList>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        ServicesCalls++;
        return Task.FromResult(OnServices());
    }

    public Task<ApiResult<Availability>> GetAvailabilityAsync(AvailabilityRequest request, CancellationToken cancellationToken = default)
    {
        AvailabilityRequests.Add(request);
        return OnAvailability(request, cancellationToken);
    }

    public Task<ApiResult<BookingTerms>> GetTermsAsync(CancellationToken cancellationToken = default)
    {
        TermsCalls++;
        return Task.FromResult(OnTerms());
    }

    public Task<ApiResult<StartBookingResponse>> StartAsync(StartBookingRequest request, CancellationToken cancellationToken = default)
    {
        StartRequests.Add(request);
        return Task.FromResult(OnStart(request));
    }

    public Task<ApiResult<VerifyResponse>> VerifyAsync(Guid verificationId, VerifyRequest request, CancellationToken cancellationToken = default)
    {
        VerifyRequests.Add((verificationId, request));
        return Task.FromResult(OnVerify(verificationId, request));
    }

    public Task<ApiResult<BookedVisit>> GetVisitAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        VisitRequests.Add(appointmentId);
        return Task.FromResult(OnVisit(appointmentId));
    }
}