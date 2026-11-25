using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Salon.Api.Contracts.Booking;

namespace Salon.Mobile.Core.Api;

/// <summary>
/// <see cref="IBookingApi"/> over HTTP on the salon's host. A refusal (400, 404, 409, 429, or any other status) comes back
/// as <see cref="ApiOutcome.Refused"/> with its <see cref="ApiProblem"/>; no network, an unreachable host and a call that
/// outlasts <see cref="SalonAppOptions.Timeout"/> come back as <see cref="ApiOutcome.Offline"/>. Only the caller's own
/// cancellation is thrown. Nothing is logged: bodies carry names and numbers.
/// </summary>
public sealed class BookingApiClient : IBookingApi
{
    private const string JsonMediaType = "application/json";

    private readonly HttpClient _http;
    private readonly SalonAppOptions _options;

    public BookingApiClient(HttpClient http, SalonAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        _http = http;
        _options = options;
    }

    public Task<ApiResult<ServiceList>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Get, BookingApi.Services, null, BookingJsonContext.Default.ServiceList, cancellationToken);
    }

    public Task<ApiResult<Availability>> GetAvailabilityAsync(AvailabilityRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = JsonSerializer.Serialize(request, BookingJsonContext.Default.AvailabilityRequest);
        return SendAsync(HttpMethod.Post, BookingApi.Availability, body, BookingJsonContext.Default.Availability, cancellationToken);
    }

    public Task<ApiResult<BookingTerms>> GetTermsAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Get, BookingApi.Terms, null, BookingJsonContext.Default.BookingTerms, cancellationToken);
    }

    public Task<ApiResult<StartBookingResponse>> StartAsync(StartBookingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = JsonSerializer.Serialize(request, BookingJsonContext.Default.StartBookingRequest);
        return SendAsync(HttpMethod.Post, BookingApi.Start, body, BookingJsonContext.Default.StartBookingResponse, cancellationToken);
    }

    public Task<ApiResult<VerifyResponse>> VerifyAsync(Guid verificationId, VerifyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = JsonSerializer.Serialize(request, BookingJsonContext.Default.VerifyRequest);
        return SendAsync(HttpMethod.Post, BookingApi.Verify(verificationId), body, BookingJsonContext.Default.VerifyResponse, cancellationToken);
    }

    public Task<ApiResult<BookedVisit>> GetVisitAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Get, BookingApi.Visit(appointmentId), null, BookingJsonContext.Default.BookedVisit, cancellationToken);
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, string? body, JsonTypeInfo<T> answer, CancellationToken cancellationToken)
        where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        // The paths are absolute (/api/v1/booking/...): the salon is the host, so they resolve against its root.
        using var request = new HttpRequestMessage(method, new Uri(_options.BaseAddress, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonMediaType));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, JsonMediaType);
        }

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var value = TryRead(text, answer);
                return value is null
                    ? ApiResult.Refused<T>(status, new ApiProblem(ApiProblemCodes.Unexpected, string.Empty))
                    : ApiResult.Ok(value, status);
            }

            return ApiResult.Refused<T>(status, ReadProblem(text, status));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The timeout fired, not the caller: as good as no network.
            return ApiResult.Offline<T>();
        }
        catch (HttpRequestException)
        {
            return ApiResult.Offline<T>();
        }
        catch (IOException)
        {
            return ApiResult.Offline<T>();
        }
    }

    private static ApiProblem ReadProblem(string text, int status)
    {
        var problem = TryRead(text, BookingJsonContext.Default.ApiProblem);

        // A refusal from something in front of the API (a proxy, the rate limiter) may have no body of ours.
        if (problem is null || string.IsNullOrWhiteSpace(problem.Code))
        {
            return new ApiProblem(ApiProblemCodes.ForStatus(status), string.Empty);
        }

        return problem.Message is null ? problem with { Message = string.Empty } : problem;
    }

    private static T? TryRead<T>(string text, JsonTypeInfo<T> type)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(text, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}