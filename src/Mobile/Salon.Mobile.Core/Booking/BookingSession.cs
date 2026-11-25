using System.Globalization;
using Salon.Api.Contracts.Booking;
using Salon.Mobile.Core.Api;

namespace Salon.Mobile.Core.Booking;

/// <summary>
/// What the screens share for one booking: the salon and its services (loaded once), the client's own choices for the
/// visit (the services and each "who with", the window, the day, the tapped time), and what came back on the way
/// (the last availability, the code sent, the booked appointment). One per app; the server decides everything else.
/// Holds no names or numbers: the details stay on their screen.
/// </summary>
public sealed class BookingSession
{
    private readonly SalonAppOptions _options;
    private readonly List<VisitPartRequest> _parts = [];
    private string? _notice;

    public BookingSession(SalonAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public SalonInfo? Salon { get; private set; }

    public IReadOnlyList<ServiceItem> Services { get; private set; } = [];

    public bool HasServices => Salon is not null;

    /// <summary>The services of the visit in order, each with its answer to "who with" (an id, "any", or null).</summary>
    public IReadOnlyList<VisitPartRequest> Parts => _parts;

    /// <summary>The first day of the window of days; null for today (the server's today).</summary>
    public string? From { get; set; }

    /// <summary>The day whose times are wanted; null for the first day that has any.</summary>
    public string? Date { get; set; }

    /// <summary>The time the client tapped (an instant in UTC, as sent); null while none is.</summary>
    public string? Start { get; set; }

    /// <summary>The service list is open to add a service to the visit, not to start one.</summary>
    public bool IsAdding { get; set; }

    /// <summary>The last answer the time screen showed, with Continue allowed; what the details screen books.</summary>
    public Availability? Availability { get; set; }

    public BookingTerms? Terms { get; set; }

    public Guid? VerificationId { get; private set; }

    /// <summary>Where the code went, with most digits hidden, as the server wrote it.</summary>
    public string? MaskedPhone { get; private set; }

    public Guid? AppointmentId { get; set; }

    public bool CanHoldAnotherService => Salon is { SeveralServices: true } salon && _parts.Count < salon.MaxServices;

    public void SetServices(ServiceList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        Salon = list.Salon;
        Services = list.Services;
    }

    /// <summary>A new visit of one service, nobody chosen: the window, the day and the time start again.</summary>
    public void StartVisit(Guid serviceId)
    {
        ClearVisit();
        _parts.Add(new VisitPartRequest(serviceId, null));
    }

    /// <summary>Adds a service after the others with nobody chosen (ADR 0096); the day and time are kept. False when it cannot go in.</summary>
    public bool AddService(Guid serviceId)
    {
        if (_parts.Count == 0 || !CanHoldAnotherService || _parts.Exists(p => p.ServiceId == serviceId))
        {
            return false;
        }

        _parts.Add(new VisitPartRequest(serviceId, null));
        IsAdding = false;
        Availability = null;
        return true;
    }

    public bool Contains(Guid serviceId)
    {
        return _parts.Exists(p => p.ServiceId == serviceId);
    }

    /// <summary>The answer to "who with" for one part: a person's id, <see cref="BookingApi.AnyStaff"/>, or null.</summary>
    public void SetStaff(int index, string? staff)
    {
        _parts[index] = _parts[index] with { Staff = staff };
    }

    /// <summary>Takes a part out; the next one moves up. The day and time are kept.</summary>
    public void RemovePartAt(int index)
    {
        _parts.RemoveAt(index);
        Availability = null;
    }

    /// <summary>The tapped time is gone (taken, or the code ran out): the visit and the day stay, the time goes.</summary>
    public void TimeHasGone()
    {
        Start = null;
        Availability = null;
        ClearVerification();
    }

    public void CodeSent(Guid verificationId, string? maskedPhone)
    {
        VerificationId = verificationId;
        MaskedPhone = maskedPhone;
    }

    public void ClearVerification()
    {
        VerificationId = null;
        MaskedPhone = null;
    }

    /// <summary>Back to nothing chosen; the salon, its services and its terms stay loaded.</summary>
    public void Reset()
    {
        ClearVisit();
        AppointmentId = null;
        _notice = null;
    }

    /// <summary>A message for the next screen to show when it appears (the time screen after "that time has just gone").</summary>
    public void PostNotice(string message)
    {
        _notice = message;
    }

    /// <summary>The message left for this screen, once.</summary>
    public string? TakeNotice()
    {
        var notice = _notice;
        _notice = null;
        return notice;
    }

    /// <summary>
    /// The parts to book, every "who with" said (BOOKING-API: start): the client's answer, else the one person the
    /// server named where there was nobody else, else anyone.
    /// </summary>
    public IReadOnlyList<VisitPartRequest> PartsToBook()
    {
        var shown = Availability?.Parts ?? [];
        var parts = new List<VisitPartRequest>(_parts.Count);
        for (var i = 0; i < _parts.Count; i++)
        {
            var part = _parts[i];
            var named = i < shown.Count ? shown[i].StaffId?.ToString() : null;
            parts.Add(part with { Staff = part.Staff ?? named ?? BookingApi.AnyStaff });
        }

        return parts;
    }

    /// <summary>An address the server sent, made absolute on the salon's host; null when there is none or it is not one.</summary>
    public Uri? ResolveUrl(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        return Uri.TryCreate(_options.BaseAddress, address, out var uri) ? uri : null;
    }

    /// <summary>The salon's culture name, or en-GB before the salon is known.</summary>
    public string CultureName => Salon?.Culture ?? "en-GB";

    /// <summary>The salon's currency, for prices before an availability answer says it.</summary>
    public string Currency => Salon?.Currency ?? new RegionInfo(CultureName).ISOCurrencySymbol;

    private void ClearVisit()
    {
        _parts.Clear();
        From = null;
        Date = null;
        Start = null;
        IsAdding = false;
        Availability = null;
        ClearVerification();
    }
}