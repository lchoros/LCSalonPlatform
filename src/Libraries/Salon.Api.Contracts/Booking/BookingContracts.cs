namespace Salon.Api.Contracts.Booking;

// The JSON shapes of the public booking API, /api/v1/booking on a salon's host (ADR 0097, docs/mobile/BOOKING-API.md).
// Plain records with primitive values only, so the web host and the mobile app share one definition and neither
// drags the other's libraries along. Dates are "yyyy-MM-dd" in the salon's zone; instants are ISO-8601 in UTC
// ("2026-11-26T09:45:00Z"); clock times are "HH:mm" in the salon's zone. Money is a decimal with the salon's currency
// code beside it.

/// <summary>Names used in the address and in the JSON, in one place for both sides.</summary>
public static class BookingApi
{
    public const string Root = "/api/v1/booking";
    public const string Services = Root + "/services";
    public const string Availability = Root + "/availability";
    public const string Terms = Root + "/terms";
    public const string Start = Root + "/start";

    /// <summary>The answer "anyone available" for a part of a visit; null means nothing is said yet.</summary>
    public const string AnyStaff = "any";

    public static string Verify(Guid verificationId) => $"{Root}/verify/{verificationId}";

    public static string Visit(Guid appointmentId) => $"{Root}/visits/{appointmentId}";
}

/// <summary>The salon the host belongs to, as far as booking needs to know it.</summary>
/// <param name="SeveralServices">Whether a booking may hold more than one service here (ADR 0095).</param>
/// <param name="MaxServices">The most services one booking may hold.</param>
public sealed record SalonInfo(string Name, string Culture, string Currency, string TimeZoneId, bool SeveralServices, int MaxServices);

/// <summary>One bookable service. Options of one service share <see cref="VariantGroup"/> and differ by <see cref="VariantLabel"/>.</summary>
/// <param name="PriceIsFrom">The people who offer it charge differently; <see cref="Price"/> is the lowest.</param>
/// <param name="IsPopular">The salon ticked it to show under "Popular".</param>
public sealed record ServiceItem(
    Guid Id,
    string Name,
    string? Description,
    int DurationMinutes,
    decimal Price,
    bool PriceIsFrom,
    string? CategoryName,
    string? VariantGroup,
    string? VariantLabel,
    bool IsPopular);

/// <summary>GET services: the salon and every active service, in the salon's own order (categories, then services).</summary>
public sealed record ServiceList(SalonInfo Salon, IReadOnlyList<ServiceItem> Services);

/// <summary>A person who can do a service.</summary>
/// <param name="PhotoUrl">An address on the salon's host, or null where there is no picture.</param>
public sealed record StaffItem(Guid Id, string Name, string? PhotoUrl);

/// <summary>One service of the visit being planned and the answer to "who with".</summary>
/// <param name="Staff">A person's id, <see cref="BookingApi.AnyStaff"/>, or null while nothing is chosen.</param>
public sealed record VisitPartRequest(Guid ServiceId, string? Staff);

/// <summary>POST availability: what the time screen needs for the visit as chosen so far.</summary>
/// <param name="Parts">The services in order, each with its answer; one to <see cref="SalonInfo.MaxServices"/>.</param>
/// <param name="From">First day of the window of days to show; null for today.</param>
/// <param name="Date">The day whose times are wanted; null for the first day that has any.</param>
/// <param name="Start">The time the client tapped, if any; one that is no longer free comes back as not chosen.</param>
public sealed record AvailabilityRequest(IReadOnlyList<VisitPartRequest> Parts, string? From = null, string? Date = null, string? Start = null);

public sealed record DayItem(string Date, bool HasTimes);

/// <param name="PartOfDay">"morning" (before 12:00), "afternoon" (12:00 to 16:00) or "evening".</param>
public sealed record TimeItem(string Start, string LocalTime, string PartOfDay);

/// <summary>One service of the visit as the screen shows it.</summary>
/// <param name="StaffId">The person chosen; null under "anyone" and while the answer is owed.</param>
/// <param name="Anyone">"Anyone available": chosen, or the only thing there is to say.</param>
/// <param name="NeedsStaffChoice">There is a choice of people and the client has not said yet.</param>
/// <param name="Staff">The people to offer: with a time chosen, only those free then.</param>
/// <param name="LocalStart">This service's own start once a time is chosen; null before.</param>
public sealed record VisitPartItem(
    Guid ServiceId,
    string Name,
    int DurationMinutes,
    decimal Price,
    bool PriceIsFrom,
    Guid? StaffId,
    bool Anyone,
    bool NeedsStaffChoice,
    IReadOnlyList<StaffItem> Staff,
    string? LocalStart,
    string? LocalEnd);

/// <summary>The answer to <see cref="AvailabilityRequest"/>.</summary>
/// <param name="EarlierFrom">The <see cref="AvailabilityRequest.From"/> of the window before this one; null at today.</param>
/// <param name="LaterFrom">The one after; null at the salon's booking horizon.</param>
/// <param name="ChosenStart">The tapped time when it is still free; otherwise null.</param>
/// <param name="CanContinue">A time is chosen and every "who with" is answered: the details may be asked for.</param>
/// <param name="CanAddService">The same, and the salon allows another service in this booking.</param>
public sealed record Availability(
    IReadOnlyList<DayItem> Days,
    string SelectedDate,
    string? EarlierFrom,
    string? LaterFrom,
    IReadOnlyList<TimeItem> Times,
    string? ChosenStart,
    IReadOnlyList<VisitPartItem> Parts,
    decimal Total,
    bool TotalIsFrom,
    int TotalMinutes,
    string Currency,
    bool CanContinue,
    bool CanAddService);

/// <summary>GET terms: what the details screen says before the client commits.</summary>
/// <param name="ConsentWording">The exact text beside the marketing tick; recorded with a yes.</param>
/// <param name="PrivacyUrl">The salon's privacy notice, opened in the browser.</param>
public sealed record BookingTerms(bool RequiresSmsCode, string ConsentWording, string PrivacyUrl, int FreeChangeHours, decimal LateCancellationFeePercent, decimal NoShowFeePercent);

/// <summary>POST start: the visit and the client's details. Every part's <see cref="VisitPartRequest.Staff"/> is set.</summary>
public sealed record StartBookingRequest(
    IReadOnlyList<VisitPartRequest> Parts,
    string Start,
    string FirstName,
    string LastName,
    string Phone,
    string? Email,
    bool MarketingConsent);

/// <summary>Either a code was sent (<see cref="VerificationId"/>) or, where the salon asks for none, the visit is booked.</summary>
/// <param name="MaskedPhone">Where the code went, with most digits hidden.</param>
public sealed record StartBookingResponse(Guid? VerificationId, Guid? AppointmentId, string? MaskedPhone);

public sealed record VerifyRequest(string Code);

/// <param name="Outcome">One of <see cref="VerifyOutcomes"/>.</param>
/// <param name="AppointmentId">Set when booked: the first appointment of the visit.</param>
public sealed record VerifyResponse(string Outcome, Guid? AppointmentId, int AttemptsLeft);

public static class VerifyOutcomes
{
    public const string Booked = "booked";
    public const string WrongCode = "wrongCode";
    public const string Expired = "expired";
    public const string TooManyAttempts = "tooManyAttempts";
    public const string SlotTaken = "slotTaken";
    public const string NotFound = "notFound";
}

/// <summary>GET visits/{id}: the confirmation of a booked visit.</summary>
/// <param name="ServiceName">Every service of the visit, joined ("Cut + Colour").</param>
/// <param name="ManageUrl">The client's own link on the salon's host, where the visit can be cancelled.</param>
public sealed record BookedVisit(Guid Id, string ServiceName, string StaffName, string Start, string End, string LocalDate, string LocalTime, decimal Price, string Currency, string Status, string ManageUrl);

/// <summary>Every refusal: 400 for what the client can put right, 404, 409 for a time that has just gone, 429.</summary>
/// <param name="Code">A stable word for the app to act on: "validation", "slotTaken", "tooManyCodes", "notFound".</param>
/// <param name="Message">A sentence in the salon's language, fit to show.</param>
/// <param name="Fields">For "validation": the messages by field name ("FirstName", "Phone", …).</param>
public sealed record ApiProblem(string Code, string Message, IReadOnlyDictionary<string, string[]>? Fields = null);