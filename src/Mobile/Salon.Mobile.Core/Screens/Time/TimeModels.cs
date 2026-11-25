using Salon.Mobile.Core.Mvvm;

namespace Salon.Mobile.Core.Screens.Time;

/// <summary>A day tile. <see cref="HasTimes"/> false: shown struck through, still tappable (the server says why).</summary>
/// <param name="Date">"yyyy-MM-dd" as the server sent it; what a tap sends back.</param>
/// <param name="WeekdayText">"Thu".</param>
/// <param name="DayNumberText">"26".</param>
public sealed record DayModel(string Date, string WeekdayText, string DayNumberText, bool HasTimes, bool IsSelected);

/// <summary>A free start. <see cref="LocalTime"/> is the salon's clock, as sent; <see cref="Start"/> is what a tap sends back.</summary>
public sealed record TimeSlotModel(string Start, string LocalTime, string PartOfDay, bool IsChosen);

/// <summary>A Morning / Afternoon / Evening tab; only the parts of the day that have a free time get one.</summary>
/// <param name="Key">"morning", "afternoon" or "evening", as the server names them.</param>
public sealed record PartOfDayTab(string Key, string Label, bool IsSelected, IReadOnlyList<TimeSlotModel> Times);

/// <summary>One answer in a part's staff sheet: a person, or "Anyone available" (<see cref="IsAnyone"/>).</summary>
/// <param name="ServiceId">The part it answers for (a service is in a visit once).</param>
/// <param name="Value">What the answer sends: the person's id, or "any".</param>
/// <param name="PhotoUrl">The person's picture on the salon's host; null where there is none.</param>
public sealed record StaffOption(Guid ServiceId, string Value, string Name, Uri? PhotoUrl, bool IsAnyone, bool IsChosen);

/// <summary>The sheet "Who would you like for {service}?" for one part of the visit; "Anyone available" comes first.</summary>
/// <param name="Note">With a time tapped: whose list this is ("the people free at 10:30…"); null otherwise.</param>
public sealed record StaffSheet(Guid ServiceId, string Title, string? Note, IReadOnlyList<StaffOption> Options);

/// <summary>
/// A card for one service of the visit: its name, price, own time (or length before a time is tapped) and its staff
/// line, "Staff: Anna" / "No preference" / "Not chosen yet · Required" with Choose or Change.
/// </summary>
public sealed class VisitPartCard : ObservableObject
{
    private bool _isHighlighted;

    internal VisitPartCard(int index, Guid serviceId, string name, string priceText, string durationText, string? timeText, string staffText, Uri? staffPhotoUrl, bool staffRequired, bool canChooseStaff, string staffActionText)
    {
        Index = index;
        ServiceId = serviceId;
        Name = name;
        PriceText = priceText;
        DurationText = durationText;
        TimeText = timeText;
        StaffText = staffText;
        StaffPhotoUrl = staffPhotoUrl;
        StaffRequired = staffRequired;
        CanChooseStaff = canChooseStaff;
        StaffActionText = staffActionText;
    }

    /// <summary>Its place in the visit, from 0.</summary>
    public int Index { get; }

    public Guid ServiceId { get; }

    public string Name { get; }

    /// <summary>"£45.00" or "from £16.00".</summary>
    public string PriceText { get; }

    /// <summary>"45 min".</summary>
    public string DurationText { get; }

    /// <summary>"09:45 – 10:30" once a time is tapped; null before.</summary>
    public string? TimeText { get; }

    /// <summary>What sits under the name: its own time once there is one, its length before.</summary>
    public string TimeOrDurationText => TimeText ?? DurationText;

    /// <summary>The person's name, "No preference", or "Not chosen yet"; shown after the "Staff:" label.</summary>
    public string StaffText { get; }

    public Uri? StaffPhotoUrl { get; }

    /// <summary>The answer is owed: show "Required" beside <see cref="StaffText"/>.</summary>
    public bool StaffRequired { get; }

    /// <summary>There is a sheet to open (someone to choose from).</summary>
    public bool CanChooseStaff { get; }

    /// <summary>"Choose" while the answer is owed, "Change" after.</summary>
    public string StaffActionText { get; }

    /// <summary>Continue or Add was tapped while this card's answer was owed: light its staff line up for a moment.</summary>
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set => SetProperty(ref _isHighlighted, value);
    }
}