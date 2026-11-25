using System.Net;
using System.Text;
using Salon.Api.Contracts.Booking;
using Salon.Mobile.Core.Api;

namespace Salon.Tests.Mobile.Api;

public class BookingApiClientTests
{
    private static readonly Uri Host = new("https://studio.example.com/");
    private static readonly Guid Id = new("0190a000-0000-7000-8000-000000000001");

    [Fact]
    public async Task Services_is_a_get_on_the_salons_host_and_reads_camel_case()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """
            {"salon":{"name":"Studio","culture":"en-GB","currency":"GBP","timeZoneId":"Europe/London","severalServices":true,"maxServices":4},
             "services":[{"id":"0190a000-0000-7000-8000-000000000001","name":"Cut","description":null,"durationMinutes":45,"price":45.00,
                          "priceIsFrom":false,"categoryName":"Hair","variantGroup":null,"variantLabel":null,"isPopular":true}]}
            """);

        var result = await Client(stub).GetServicesAsync();

        stub.Method.ShouldBe(HttpMethod.Get);
        stub.Uri.ShouldBe(new Uri("https://studio.example.com/api/v1/booking/services"));
        stub.Accept.ShouldContain("application/json");
        result.IsOk.ShouldBeTrue();
        result.StatusCode.ShouldBe(200);
        result.Value.Salon.MaxServices.ShouldBe(4);
        result.Value.Services.ShouldHaveSingleItem().IsPopular.ShouldBeTrue();
        result.Value.Services[0].Price.ShouldBe(45m);
    }

    [Fact]
    public async Task Availability_posts_the_choices_in_camel_case()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """
            {"days":[{"date":"2026-11-26","hasTimes":true}],"selectedDate":"2026-11-26","earlierFrom":null,"laterFrom":"2026-12-03",
             "times":[{"start":"2026-11-26T09:00:00Z","localTime":"09:00","partOfDay":"morning"}],"chosenStart":null,
             "parts":[{"serviceId":"0190a000-0000-7000-8000-000000000001","name":"Cut","durationMinutes":45,"price":45,"priceIsFrom":false,
                       "staffId":null,"anyone":false,"needsStaffChoice":true,"staff":[{"id":"0190b000-0000-7000-8000-00000000000a","name":"Anna","photoUrl":null}],
                       "localStart":null,"localEnd":null}],
             "total":45,"totalIsFrom":false,"totalMinutes":45,"currency":"GBP","canContinue":false,"canAddService":false}
            """);

        var request = new AvailabilityRequest([new VisitPartRequest(Id, BookingApi.AnyStaff)], Date: "2026-11-26", Start: "2026-11-26T09:00:00Z");
        var result = await Client(stub).GetAvailabilityAsync(request);

        stub.Method.ShouldBe(HttpMethod.Post);
        stub.Uri!.AbsolutePath.ShouldBe("/api/v1/booking/availability");
        stub.ContentType.ShouldBe("application/json");
        stub.Body.ShouldBe("""{"parts":[{"serviceId":"0190a000-0000-7000-8000-000000000001","staff":"any"}],"from":null,"date":"2026-11-26","start":"2026-11-26T09:00:00Z"}""");
        result.IsOk.ShouldBeTrue();
        result.Value.Parts.ShouldHaveSingleItem().NeedsStaffChoice.ShouldBeTrue();
        result.Value.Parts[0].Staff.ShouldHaveSingleItem().Name.ShouldBe("Anna");
        result.Value.LaterFrom.ShouldBe("2026-12-03");
    }

    [Fact]
    public async Task Terms_is_a_get()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """
            {"requiresSmsCode":true,"consentWording":"I agree.","privacyUrl":"/privacy","freeChangeHours":24,"lateCancellationFeePercent":50,"noShowFeePercent":100}
            """);

        var result = await Client(stub).GetTermsAsync();

        stub.Method.ShouldBe(HttpMethod.Get);
        stub.Uri!.AbsolutePath.ShouldBe("/api/v1/booking/terms");
        result.Value!.ConsentWording.ShouldBe("I agree.");
        result.Value.NoShowFeePercent.ShouldBe(100m);
    }

    [Fact]
    public async Task Start_posts_the_details_in_camel_case()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"verificationId":"0190c000-0000-7000-8000-000000000001","appointmentId":null,"maskedPhone":"+44 ••• 789"}""");
        var request = new StartBookingRequest([new VisitPartRequest(Id, "any")], "2026-11-26T09:00:00Z", "Ada", "Lovelace", "07700900789", null, true);

        var result = await Client(stub).StartAsync(request);

        stub.Method.ShouldBe(HttpMethod.Post);
        stub.Uri!.AbsolutePath.ShouldBe("/api/v1/booking/start");
        stub.Body.ShouldBe("""{"parts":[{"serviceId":"0190a000-0000-7000-8000-000000000001","staff":"any"}],"start":"2026-11-26T09:00:00Z","firstName":"Ada","lastName":"Lovelace","phone":"07700900789","email":null,"marketingConsent":true}""");
        result.Value!.VerificationId.ShouldBe(new Guid("0190c000-0000-7000-8000-000000000001"));
        result.Value.AppointmentId.ShouldBeNull();
    }

    [Fact]
    public async Task Verify_posts_the_code_to_the_verifications_address()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"outcome":"wrongCode","appointmentId":null,"attemptsLeft":2}""");

        var result = await Client(stub).VerifyAsync(Id, new VerifyRequest("123456"));

        stub.Method.ShouldBe(HttpMethod.Post);
        stub.Uri!.AbsolutePath.ShouldBe($"/api/v1/booking/verify/{Id}");
        stub.Body.ShouldBe("""{"code":"123456"}""");
        result.Value!.Outcome.ShouldBe(VerifyOutcomes.WrongCode);
        result.Value.AttemptsLeft.ShouldBe(2);
    }

    [Fact]
    public async Task Visit_is_a_get_by_the_appointments_id()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """
            {"id":"0190a000-0000-7000-8000-000000000001","serviceName":"Cut + Colour","staffName":"Anna","start":"2026-11-26T09:45:00Z","end":"2026-11-26T11:30:00Z",
             "localDate":"2026-11-26","localTime":"09:45","price":105,"currency":"GBP","status":"booked","manageUrl":"https://studio.example.com/book/manage/x"}
            """);

        var result = await Client(stub).GetVisitAsync(Id);

        stub.Method.ShouldBe(HttpMethod.Get);
        stub.Uri!.AbsolutePath.ShouldBe($"/api/v1/booking/visits/{Id}");
        result.Value!.ServiceName.ShouldBe("Cut + Colour");
        result.Value.Price.ShouldBe(105m);
    }

    [Fact]
    public async Task A_validation_refusal_comes_back_with_its_status_message_and_fields()
    {
        var stub = new StubHandler(HttpStatusCode.BadRequest, """{"code":"validation","message":"Check the form.","fields":{"Phone":["Enter a valid mobile number."],"Email":["Enter a valid email address."]}}""");

        var result = await Client(stub).StartAsync(new StartBookingRequest([], "x", "a", "b", "c", null, false));

        result.IsOk.ShouldBeFalse();
        result.IsRefused.ShouldBeTrue();
        result.IsOffline.ShouldBeFalse();
        result.StatusCode.ShouldBe(400);
        result.ProblemCode.ShouldBe(ApiProblemCodes.Validation);
        result.Problem!.Message.ShouldBe("Check the form.");
        result.Problem.Fields!["Phone"].ShouldBe(["Enter a valid mobile number."]);
        result.Problem.Fields["Email"].ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "notFound")]
    [InlineData(HttpStatusCode.Conflict, "slotTaken")]
    [InlineData(HttpStatusCode.TooManyRequests, "tooManyCodes")]
    public async Task Each_refusal_the_doc_names_is_a_result_not_an_exception(HttpStatusCode status, string code)
    {
        var stub = new StubHandler(status, $$"""{"code":"{{code}}","message":"In the salon's words."}""");

        var result = await Client(stub).GetTermsAsync();

        result.Outcome.ShouldBe(ApiOutcome.Refused);
        result.StatusCode.ShouldBe((int)status);
        result.ProblemCode.ShouldBe(code);
        result.Problem!.Message.ShouldBe("In the salon's words.");
        result.Problem.Fields.ShouldBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "<html>Not found</html>", "notFound")]
    [InlineData(HttpStatusCode.TooManyRequests, "", "tooManyCodes")]
    [InlineData(HttpStatusCode.Conflict, "{}", "slotTaken")]
    [InlineData(HttpStatusCode.InternalServerError, "", "unexpected")]
    [InlineData(HttpStatusCode.BadGateway, "<html>Bad gateway</html>", "unexpected")]
    public async Task A_refusal_without_a_readable_problem_is_named_by_its_status(HttpStatusCode status, string body, string code)
    {
        var stub = new StubHandler(status, body, "text/html");

        var result = await Client(stub).GetServicesAsync();

        result.IsRefused.ShouldBeTrue();
        result.StatusCode.ShouldBe((int)status);
        result.ProblemCode.ShouldBe(code);
        result.Problem!.Message.ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task A_success_that_cannot_be_read_is_unexpected_not_a_crash(string body)
    {
        var stub = new StubHandler(HttpStatusCode.OK, body);

        var result = await Client(stub).GetServicesAsync();

        result.IsRefused.ShouldBeTrue();
        result.ProblemCode.ShouldBe(ApiProblemCodes.Unexpected);
    }

    [Fact]
    public async Task No_network_is_offline()
    {
        var stub = new StubHandler(_ => throw new HttpRequestException("No route to host"));

        var result = await Client(stub).GetServicesAsync();

        result.Outcome.ShouldBe(ApiOutcome.Offline);
        result.IsOffline.ShouldBeTrue();
        result.StatusCode.ShouldBe(0);
        result.Problem.ShouldBeNull();
    }

    [Fact]
    public async Task A_broken_connection_while_reading_is_offline()
    {
        var stub = new StubHandler(_ => throw new IOException("Connection reset"));

        var result = await Client(stub).GetTermsAsync();

        result.IsOffline.ShouldBeTrue();
    }

    [Fact]
    public async Task A_call_that_outlasts_the_timeout_is_offline()
    {
        var stub = new StubHandler(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await Client(stub, TimeSpan.FromMilliseconds(50)).GetServicesAsync();

        result.IsOffline.ShouldBeTrue();
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_thrown_not_offline()
    {
        var stub = new StubHandler(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cancel = new CancellationTokenSource();
        var call = Client(stub).GetAvailabilityAsync(new AvailabilityRequest([new VisitPartRequest(Id, null)]), cancel.Token);

        await cancel.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(call);
    }

    [Fact]
    public async Task The_paths_resolve_against_the_hosts_root()
    {
        var stub = new StubHandler(HttpStatusCode.OK, "{}");

        await Client(stub, host: new Uri("https://studio.example.com/somewhere/")).GetServicesAsync();

        stub.Uri.ShouldBe(new Uri("https://studio.example.com/api/v1/booking/services"));
    }

    private static BookingApiClient Client(StubHandler stub, TimeSpan? timeout = null, Uri? host = null)
    {
        var http = new HttpClient(stub);
        return new BookingApiClient(http, new SalonAppOptions { BaseAddress = host ?? Host, Timeout = timeout ?? TimeSpan.FromSeconds(5) });
    }

    /// <summary>Answers every request with one response and keeps what was sent.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> _answer;

        public StubHandler(HttpStatusCode status, string body, string mediaType = "application/json")
            : this(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) }))
        {
        }

        public StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> answer)
        {
            _answer = answer;
        }

        public HttpMethod? Method { get; private set; }

        public Uri? Uri { get; private set; }

        public string? Body { get; private set; }

        public string? ContentType { get; private set; }

        public List<string> Accept { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            Accept.AddRange(request.Headers.Accept.Select(a => a.MediaType ?? string.Empty));
            if (request.Content is not null)
            {
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
                ContentType = request.Content.Headers.ContentType?.MediaType;
            }

            return await _answer(cancellationToken);
        }
    }
}