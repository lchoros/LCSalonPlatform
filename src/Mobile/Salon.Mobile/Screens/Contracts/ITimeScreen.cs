using System.Windows.Input;

namespace Salon.Mobile.Screens.Contracts;

/// <summary>
/// Step 2: the day, the time and who with, for every service of the visit (BOOKING-API.md, "Time"). Each tap is the
/// same availability call again; the answer replaces the screen's state.
/// </summary>
public interface ITimeScreen : IScreen
{
    /// <summary>"yyyy-MM-dd": the day whose times show; the month in the title is its month.</summary>
    string SelectedDate { get; }

    /// <summary>The window of days can move back (<c>earlierFrom</c> is set).</summary>
    bool CanGoEarlier { get; }

    /// <summary>The window of days can move on (<c>laterFrom</c> is set).</summary>
    bool CanGoLater { get; }

    IReadOnlyList<IDayTile> Days { get; }

    /// <summary>Only the parts of the day that have a free time.</summary>
    IReadOnlyList<IPartOfDayTab> PartsOfDay { get; }

    /// <summary>The free times of the chosen part of the day.</summary>
    IReadOnlyList<ITimeChip> Times { get; }

    /// <summary>One card per service of the visit, in order.</summary>
    IReadOnlyList<IVisitPart> Parts { get; }

    IPriced Total { get; }

    int TotalMinutes { get; }

    /// <summary>A time is chosen and every "who with" is answered. Continue stays tappable either way.</summary>
    bool CanContinue { get; }

    /// <summary>The same, and the salon allows another service. "+ Add another service" stays tappable either way.</summary>
    bool CanAddService { get; }

    /// <summary>What to say at the top after a tap that came too early.</summary>
    TimeHint Hint { get; }

    /// <summary>The "who with" sheet of one service while it is open; null while it is closed.</summary>
    IStaffSheet? StaffSheet { get; }

    ICommand EarlierCommand { get; }

    ICommand LaterCommand { get; }

    /// <summary>Parameter: the <see cref="IDayTile"/>.</summary>
    ICommand ChooseDayCommand { get; }

    /// <summary>Parameter: the <see cref="IPartOfDayTab"/>.</summary>
    ICommand ChoosePartOfDayCommand { get; }

    /// <summary>Parameter: the <see cref="ITimeChip"/>.</summary>
    ICommand ChooseTimeCommand { get; }

    /// <summary>Parameter: the <see cref="IVisitPart"/> whose Choose or Change was tapped; opens its sheet.</summary>
    ICommand ChooseStaffCommand { get; }

    /// <summary>Parameter: the <see cref="IVisitPart"/> to take out (only offered when there is more than one).</summary>
    ICommand RemovePartCommand { get; }

    /// <summary>Goes to the service list in "add" mode when allowed; otherwise sets <see cref="Hint"/>.</summary>
    ICommand AddServiceCommand { get; }

    /// <summary>Goes on to the details when allowed; otherwise sets <see cref="Hint"/> and flags the parts concerned.</summary>
    ICommand ContinueCommand { get; }

    /// <summary>The cross at the top: asks "Discard booking?" (INavigator) and, on yes, starts over.</summary>
    ICommand CloseCommand { get; }
}

public enum TimeHint
{
    None,
    ChooseTime,
    ChooseStaff,
    ChooseTimeAndStaff,

    /// <summary>"+ Add another service" was tapped before a time was chosen.</summary>
    ChooseTimeBeforeAdding,

    /// <summary>The visit holds as many services as the salon allows.</summary>
    VisitFull,
}

public interface IDayTile
{
    /// <summary>"yyyy-MM-dd".</summary>
    string LocalDate { get; }

    bool HasTimes { get; }

    bool IsSelected { get; }
}

public enum PartOfDay
{
    Morning,
    Afternoon,
    Evening,
}

public interface IPartOfDayTab
{
    PartOfDay Part { get; }

    bool IsSelected { get; }
}

public interface ITimeChip
{
    /// <summary>"HH:mm".</summary>
    string LocalTime { get; }

    bool IsSelected { get; }
}

/// <summary>One service of the visit, as its card shows it.</summary>
public interface IVisitPart
{
    string Name { get; }

    int DurationMinutes { get; }

    IPriced Price { get; }

    /// <summary>"HH:mm", this service's own start once a time is chosen; null before.</summary>
    string? LocalStart { get; }

    string? LocalEnd { get; }

    /// <summary>The person chosen; null under "anyone available" and while the answer is owed.</summary>
    string? StaffName { get; }

    /// <summary>An address on the salon's host (or a bundled image name in the samples); null for no picture.</summary>
    string? StaffPhotoUrl { get; }

    /// <summary>"Anyone available" is the answer.</summary>
    bool Anyone { get; }

    /// <summary>There is a choice of people and the client has not said yet: "Not chosen yet · Required".</summary>
    bool NeedsStaffChoice { get; }

    /// <summary>There is more than one service in the visit, so this one can be taken out.</summary>
    bool CanRemove { get; }

    /// <summary>Continue was tapped while this card still owes an answer: its staff line stands out.</summary>
    bool IsFlagged { get; }
}

/// <summary>"Who with" for one service: full-screen, people side by side, "Anyone available" first.</summary>
public interface IStaffSheet
{
    /// <summary>The service the question is about.</summary>
    string ServiceName { get; }

    /// <summary>"HH:mm" when a time is chosen (the people shown are those free then); null otherwise.</summary>
    string? AtTime { get; }

    /// <summary>"Anyone available" first, then the people.</summary>
    IReadOnlyList<IStaffChoice> Choices { get; }

    /// <summary>Parameter: the <see cref="IStaffChoice"/>; answers and closes the sheet.</summary>
    ICommand ChooseCommand { get; }

    ICommand CloseCommand { get; }
}

public interface IStaffChoice
{
    /// <summary>"Anyone available" rather than a person.</summary>
    bool IsAnyone { get; }

    /// <summary>The person's name; null for <see cref="IsAnyone"/>.</summary>
    string? Name { get; }

    /// <summary>As <see cref="IVisitPart.StaffPhotoUrl"/>.</summary>
    string? PhotoUrl { get; }

    bool IsSelected { get; }
}