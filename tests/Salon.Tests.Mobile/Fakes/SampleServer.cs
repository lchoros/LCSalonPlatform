using System.Globalization;
using Salon.Api.Contracts.Booking;

namespace Salon.Tests.Mobile.Fakes;

/// <summary>Known ids and shapes the tests share.</summary>
internal static class Sample
{
    public static readonly Guid Cut = new("0190a000-0000-7000-8000-000000000001");
    public static readonly Guid Colour = new("0190a000-0000-7000-8000-000000000002");
    public static readonly Guid GelShort = new("0190a000-0000-7000-8000-000000000003");
    public static readonly Guid GelLong = new("0190a000-0000-7000-8000-000000000004");
    public static readonly Guid Beard = new("0190a000-0000-7000-8000-000000000005");

    public static readonly Guid Anna = new("0190b000-0000-7000-8000-00000000000a");
    public static readonly Guid Ben = new("0190b000-0000-7000-8000-00000000000b");

    public static readonly Guid VerificationId = new("0190c000-0000-7000-8000-000000000001");
    public static readonly Guid AppointmentId = new("0190d000-0000-7000-8000-000000000001");

    public static SalonInfo Salon(string culture = "en-GB", string currency = "GBP", bool severalServices = true, int maxServices = 4)
    {
        return new SalonInfo("Studio Anna", culture, currency, "Europe/London", severalServices, maxServices);
    }

    public static List<ServiceItem> Services()
    {
        return
        [
            new ServiceItem(Cut, "Cut", "A wash, a cut and a blow-dry.", 45, 45m, false, "Hair", null, null, true),
            new ServiceItem(Colour, "Colour", null, 60, 60m, true, "Hair", null, null, false),
            new ServiceItem(GelShort, "Gel nails short", null, 30, 25m, false, "Nails", "Gel nails", "Short", false),
            new ServiceItem(GelLong, "Gel nails long", "Long-lasting colour.", 60, 35m, false, "Nails", "Gel nails", "Long", false),
            new ServiceItem(Beard, "Beard trim", null, 20, 16m, true, null, null, null, false),
        ];
    }

    public static BookedVisit Visit(string manageUrl = "/book/manage/abc123")
    {
        return new BookedVisit(AppointmentId, "Cut + Colour", "Anna", "2026-11-26T09:45:00Z", "2026-11-26T11:30:00Z", "2026-11-26", "09:45", 105m, "GBP", "booked", manageUrl);
    }

    public static VisitPartItem Part(Guid serviceId, string name, bool needsStaffChoice = true, Guid? staffId = null, bool anyone = false, string? localStart = null, string? localEnd = null)
    {
        return new VisitPartItem(serviceId, name, 45, 45m, false, staffId, anyone, needsStaffChoice, [new StaffItem(Anna, "Anna", "/media/staff/anna.jpg"), new StaffItem(Ben, "Ben", null)], localStart, localEnd);
    }

    public static Availability Answer(IReadOnlyList<VisitPartItem> parts, string? chosenStart = null, bool canContinue = false, bool canAddService = false, IReadOnlyList<TimeItem>? times = null, string selectedDate = "2026-11-26")
    {
        return new Availability(
            [new DayItem("2026-11-26", true), new DayItem("2026-11-27", true)],
            selectedDate,
            null,
            "2026-12-03",
            times ?? [new TimeItem("2026-11-26T09:00:00Z", "09:00", "morning"), new TimeItem("2026-11-26T13:00:00Z", "13:00", "afternoon")],
            chosenStart,
            parts,
            parts.Sum(p => p.Price),
            parts.Any(p => p.PriceIsFrom),
            parts.Sum(p => p.DurationMinutes),
            "GBP",
            canContinue,
            canAddService);
    }
}

/// <summary>
/// Answers availability the way the server does, closely enough for the screens: a week from <c>From</c> (today is
/// 2026-11-26), Saturday the 28th without times, four times a day, Cut and Colour by Anna or Ben, the beard only by
/// Ben (nothing to ask), "who with" owed until answered, Continue once a time is chosen and every answer is in.
/// </summary>
internal sealed class SampleServer
{
    public const string Today = "2026-11-26";

    private static readonly (string Local, string Part)[] DayTimes =
    [
        ("09:00", "morning"),
        ("09:45", "morning"),
        ("13:00", "afternoon"),
        ("18:00", "evening"),
    ];

    public SalonInfo Salon { get; set; } = Sample.Salon();

    public List<ServiceItem> Services { get; set; } = Sample.Services();

    public BookingTerms Terms { get; set; } = new(true, "I agree to Studio Anna sending me news and offers.", "/privacy", 24, 50m, 100m);

    /// <summary>Starts that are no longer free, as instants.</summary>
    public HashSet<string> Taken { get; } = new(StringComparer.Ordinal);

    public ServiceList ServiceList()
    {
        return new ServiceList(Salon, Services);
    }

    public Availability Availability(AvailabilityRequest request)
    {
        var from = DateOnly.ParseExact(request.From ?? Today, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var days = Enumerable.Range(0, 7)
            .Select(i => from.AddDays(i))
            .Select(d => new DayItem(Text(d), d.DayOfWeek != DayOfWeek.Saturday))
            .ToList();
        var selected = request.Date ?? days.First(d => d.HasTimes).Date;
        var hasTimes = days.Any(d => d.Date == selected && d.HasTimes);

        var times = hasTimes
            ? DayTimes.Select(t => new TimeItem($"{selected}T{t.Local}:00Z", t.Local, t.Part)).Where(t => !Taken.Contains(t.Start)).ToList()
            : [];
        var chosen = times.FirstOrDefault(t => t.Start == request.Start);

        var parts = new List<VisitPartItem>();
        var clock = chosen is null ? (TimeOnly?)null : TimeOnly.ParseExact(chosen.LocalTime, "HH:mm", CultureInfo.InvariantCulture);
        foreach (var part in request.Parts)
        {
            var service = Services.First(s => s.Id == part.ServiceId);
            var offerers = OfferedBy(service.Id);
            Guid? staffId = Guid.TryParse(part.Staff, out var id) && offerers.Exists(s => s.Id == id) ? id : null;
            var anyone = part.Staff == BookingApi.AnyStaff;
            if (staffId is null && !anyone && offerers.Count == 1)
            {
                staffId = offerers[0].Id;
            }

            var needs = staffId is null && !anyone && offerers.Count > 1;
            string? localStart = null;
            string? localEnd = null;
            if (clock is { } start)
            {
                var end = start.AddMinutes(service.DurationMinutes);
                localStart = start.ToString("HH:mm", CultureInfo.InvariantCulture);
                localEnd = end.ToString("HH:mm", CultureInfo.InvariantCulture);
                clock = end;
            }

            parts.Add(new VisitPartItem(service.Id, service.Name, service.DurationMinutes, service.Price, service.PriceIsFrom, staffId, anyone, needs, offerers, localStart, localEnd));
        }

        var canContinue = chosen is not null && parts.TrueForAll(p => !p.NeedsStaffChoice);
        return new Availability(
            days,
            selected,
            request.From is null || request.From == Today ? null : Text(from.AddDays(-7)),
            Text(from.AddDays(7)),
            times,
            chosen?.Start,
            parts,
            parts.Sum(p => p.Price),
            parts.Exists(p => p.PriceIsFrom),
            parts.Sum(p => p.DurationMinutes),
            Salon.Currency,
            canContinue,
            canContinue && Salon.SeveralServices && parts.Count < Salon.MaxServices);
    }

    private static List<StaffItem> OfferedBy(Guid serviceId)
    {
        var anna = new StaffItem(Sample.Anna, "Anna", "/media/staff/anna.jpg");
        var ben = new StaffItem(Sample.Ben, "Ben", null);
        return serviceId == Sample.Beard ? [ben] : [anna, ben];
    }

    private static string Text(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}