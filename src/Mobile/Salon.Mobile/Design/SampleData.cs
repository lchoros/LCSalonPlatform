using Salon.Api.Contracts.Booking;
using Salon.Mobile.Screens.Contracts;

namespace Salon.Mobile.Design;

/// <summary>
/// A made-up salon in the API's own shapes (Salon.Api.Contracts), so the samples read the way the real screen models
/// will. This is the salon's data, not the app's words: on a real phone it comes from the server in the salon's
/// language, which is why it is not in AppResources.
/// </summary>
internal static class SampleData
{
    public const string Currency = "GBP";

    public static readonly SalonInfo Salon = new("Studio Lumen", "en-GB", Currency, "Europe/London", SeveralServices: true, MaxServices: 3);

    public static readonly StaffItem Anna = new(Guid.Parse("0193a0c0-0000-7000-8000-000000000001"), "Anna Kowalska", "staff_anna.png");
    public static readonly StaffItem Beth = new(Guid.Parse("0193a0c0-0000-7000-8000-000000000002"), "Beth Morgan", null);
    public static readonly StaffItem Chloe = new(Guid.Parse("0193a0c0-0000-7000-8000-000000000003"), "Chloe Evans", "staff_chloe.png");
    public static readonly StaffItem Daniel = new(Guid.Parse("0193a0c0-0000-7000-8000-000000000004"), "Daniel Price", null);

    public static readonly ServiceItem Cut = new(
        Guid.Parse("0193a0c0-0000-7000-8000-000000000101"),
        "Ladies' cut and blow-dry",
        "A consultation, a wash with a scalp massage, a precision cut to suit your face and hair type, and a blow-dry " +
        "finish. Please arrive with your hair as you usually wear it, so we can see how it falls. Allow a little longer " +
        "for very long or thick hair.",
        45,
        38m,
        PriceIsFrom: true,
        "Hair",
        VariantGroup: null,
        VariantLabel: null,
        IsPopular: true);

    public static readonly ServiceItem RootColour = new(Guid.Parse("0193a0c0-0000-7000-8000-000000000102"), "Colour", null, 60, 55m, false, "Hair", "Colour", "Root touch-up", false);
    public static readonly ServiceItem FullColour = new(Guid.Parse("0193a0c0-0000-7000-8000-000000000103"), "Colour", null, 120, 85m, true, "Hair", "Colour", "Full head", false);
    public static readonly ServiceItem Balayage = new(Guid.Parse("0193a0c0-0000-7000-8000-000000000104"), "Colour", null, 180, 140m, true, "Hair", "Colour", "Balayage", false);

    public static readonly ServiceItem Manicure = new(
        Guid.Parse("0193a0c0-0000-7000-8000-000000000201"),
        "Gel manicure",
        "Shaping, cuticle care and a gel colour that lasts up to three weeks.",
        40,
        16m,
        PriceIsFrom: true,
        "Nails",
        null,
        null,
        IsPopular: true);

    public static readonly ServiceItem Brows = new(Guid.Parse("0193a0c0-0000-7000-8000-000000000301"), "Brow shape and tint", null, 20, 18m, false, "Brows", null, null, false);

    public static readonly ServiceList Services = new(Salon, [Cut, RootColour, FullColour, Balayage, Manicure, Brows]);

    /// <summary>Who offers what. One person for the brows, so that card has nothing to ask.</summary>
    public static IReadOnlyList<StaffItem> StaffFor(Guid serviceId)
    {
        if (serviceId == Manicure.Id)
        {
            return [Beth, Chloe];
        }

        return serviceId == Brows.Id ? [Daniel] : [Anna, Beth, Chloe];
    }

    public static IPriced Price(decimal price, bool isFrom) => new SamplePrice(price, isFrom, Currency);
}

internal sealed record SamplePrice(decimal Price, bool PriceIsFrom, string Currency) : IPriced;

internal sealed record SampleVisitSummary(string ServiceName, string? StaffName, string LocalDate, string LocalTime, IPriced Price) : IVisitSummary;

/// <summary>What the client has chosen so far, shared by the sample screens (the real models keep their own).</summary>
internal sealed class SampleBooking
{
    private readonly List<(ServiceItem Service, string? Staff)> _parts = [(SampleData.Cut, SampleData.Anna.Id.ToString()), (SampleData.Manicure, null)];

    /// <summary>The services in order with the answer to "who with": a person's id, "any", or null while owed.</summary>
    public IReadOnlyList<(ServiceItem Service, string? Staff)> Parts => _parts;

    public string? Date { get; set; }

    public string? Time { get; set; }

    public bool IsAdding { get; set; }

    public string Phone { get; set; } = string.Empty;

    public void Start(ServiceItem service)
    {
        _parts.Clear();
        _parts.Add((service, null));
        Date = null;
        Time = null;
        IsAdding = false;
    }

    public void Add(ServiceItem service)
    {
        _parts.Add((service, null));
        IsAdding = false;
    }

    public void Remove(Guid serviceId) => _parts.RemoveAll(part => part.Service.Id == serviceId);

    public void Answer(Guid serviceId, string staff)
    {
        var index = _parts.FindIndex(part => part.Service.Id == serviceId);
        if (index >= 0)
        {
            _parts[index] = (_parts[index].Service, staff);
        }
    }

    public void Reset() => Start(SampleData.Cut);

    public IVisitSummary Summary()
    {
        var names = string.Join(" + ", _parts.Select(part => part.Service.VariantLabel is { } label ? $"{part.Service.Name} ({label})" : part.Service.Name));
        var people = _parts
            .Select(part => Guid.TryParse(part.Staff, out var id) ? SampleData.StaffFor(part.Service.Id).FirstOrDefault(person => person.Id == id)?.Name : null)
            .OfType<string>()
            .Distinct()
            .ToList();
        var total = _parts.Sum(part => part.Service.Price);
        var isFrom = _parts.Any(part => part.Service.PriceIsFrom);
        return new SampleVisitSummary(
            names,
            people.Count == 0 ? null : string.Join(", ", people),
            Date ?? DateOnly.FromDateTime(DateTime.Today).AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            Time ?? "09:45",
            SampleData.Price(total, isFrom));
    }
}