using System.Globalization;
using System.Windows.Input;
using Salon.Api.Contracts.Booking;
using Salon.Mobile.Core.Api;
using Salon.Mobile.Core.Booking;
using Salon.Mobile.Core.Localization;
using Salon.Mobile.Core.Mvvm;
using Salon.Mobile.Core.Navigation;

namespace Salon.Mobile.Core.Screens.Time;

/// <summary>
/// The time screen (BOOKING-API step 2). It holds only the client's choices (in <see cref="BookingSession"/>: the
/// parts with their "who with", the window, the day, the tapped time) and replaces everything it shows from each
/// <see cref="Availability"/> answer. Every tap is the same call again, and the last tap wins: a call still on its way
/// is cancelled and its answer, should it come, is dropped.
/// Continue and "Add another service" are always tappable: tapped before the server allows them they say what is
/// missing (ADR 0096) instead of going on; the view greys them by <see cref="CanContinue"/> and <see cref="CanAddService"/>.
/// </summary>
public sealed class TimeScreen : ObservableObject, IDisposable
{
    private const string Morning = "morning";
    private const string Afternoon = "afternoon";
    private const string Evening = "evening";

    private static readonly string[] PartsOfDayOrder = [Morning, Afternoon, Evening];

    private readonly IBookingApi _api;
    private readonly INavigator _navigator;
    private readonly BookingSession _session;
    private readonly Dictionary<Guid, StaffItem> _staffSeen = [];
    private readonly Lock _gate = new();

    private CancellationTokenSource? _inFlight;
    private Availability? _answer;
    private string? _selectedPartOfDay;

    private bool _isBusy;
    private string? _errorMessage;
    private string? _message;
    private string _monthTitle = string.Empty;
    private IReadOnlyList<DayModel> _days = [];
    private bool _canGoEarlier;
    private bool _canGoLater;
    private IReadOnlyList<PartOfDayTab> _partsOfDay = [];
    private IReadOnlyList<TimeSlotModel> _times = [];
    private string? _noTimesText;
    private string? _chosenTimeText;
    private IReadOnlyList<VisitPartCard> _cards = [];
    private string _totalText = string.Empty;
    private string _totalDurationText = string.Empty;
    private bool _canContinue;
    private bool _canAddService;
    private bool _showAddService;
    private StaffSheet? _staffSheet;
    private VisitPartCard? _highlightedCard;

    public TimeScreen(IBookingApi api, INavigator navigator, BookingSession session, IAppText text)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(text);
        _api = api;
        _navigator = navigator;
        _session = session;
        Text = text;

        LoadCommand = new AsyncCommand(LoadAsync);
        RetryCommand = new AsyncCommand(RefreshAsync);
        PickDayCommand = new AsyncCommand<DayModel>(PickDayAsync);
        EarlierCommand = new AsyncCommand(EarlierAsync);
        LaterCommand = new AsyncCommand(LaterAsync);
        SelectPartOfDayCommand = new RelayCommand<PartOfDayTab>(SelectPartOfDay);
        PickTimeCommand = new AsyncCommand<TimeSlotModel>(PickTimeAsync);
        OpenStaffChoiceCommand = new RelayCommand<VisitPartCard>(OpenStaffChoice);
        ChooseStaffCommand = new AsyncCommand<StaffOption>(ChooseStaffAsync);
        CloseStaffChoiceCommand = new RelayCommand(CloseStaffChoice);
        RemovePartCommand = new AsyncCommand<VisitPartCard>(RemovePartAsync);
        AddServiceCommand = new AsyncCommand(AddServiceAsync);
        ContinueCommand = new AsyncCommand(ContinueAsync);
        LeaveCommand = new AsyncCommand(LeaveAsync);
        DismissMessageCommand = new RelayCommand(DismissMessage);
    }

    /// <summary>The app's words: bind labels as <c>Text[key]</c> (<see cref="TextKeys"/>).</summary>
    public IAppText Text { get; }

    /// <summary>A call is on its way: grey the days and times out a little; the screen stays as it is.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>The first answer never came (offline, refused): the whole screen says so, with <see cref="RetryCommand"/>.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => _errorMessage is not null;

    /// <summary>
    /// A message over the top of the screen: what is missing, "that time has just gone", offline after a tap. Cleared
    /// by the next tap or <see cref="DismissMessageCommand"/>.
    /// </summary>
    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>An answer has come: the days, times and cards are there to show.</summary>
    public bool HasAnswer => _answer is not null;

    /// <summary>"November 2026" from the selected day, in the salon's language.</summary>
    public string MonthTitle
    {
        get => _monthTitle;
        private set => SetProperty(ref _monthTitle, value);
    }

    public IReadOnlyList<DayModel> Days
    {
        get => _days;
        private set => SetProperty(ref _days, value);
    }

    public bool CanGoEarlier
    {
        get => _canGoEarlier;
        private set => SetProperty(ref _canGoEarlier, value);
    }

    public bool CanGoLater
    {
        get => _canGoLater;
        private set => SetProperty(ref _canGoLater, value);
    }

    /// <summary>Morning / Afternoon / Evening, only those with a free time; one is selected.</summary>
    public IReadOnlyList<PartOfDayTab> PartsOfDay
    {
        get => _partsOfDay;
        private set => SetProperty(ref _partsOfDay, value);
    }

    /// <summary>The times of the selected part of the day.</summary>
    public IReadOnlyList<TimeSlotModel> Times
    {
        get => _times;
        private set => SetProperty(ref _times, value);
    }

    /// <summary>"No free times on this day…" when the day has none; null when it has.</summary>
    public string? NoTimesText
    {
        get => _noTimesText;
        private set => SetProperty(ref _noTimesText, value);
    }

    /// <summary>"09:45 – 11:00" for the whole visit once a time is tapped; null before.</summary>
    public string? ChosenTimeText
    {
        get => _chosenTimeText;
        private set => SetProperty(ref _chosenTimeText, value);
    }

    /// <summary>A card per service of the visit, in order.</summary>
    public IReadOnlyList<VisitPartCard> Cards
    {
        get => _cards;
        private set => SetProperty(ref _cards, value);
    }

    /// <summary>The first card whose answer is owed, after Continue or Add was tapped too early; scroll it into sight.</summary>
    public VisitPartCard? HighlightedCard
    {
        get => _highlightedCard;
        private set => SetProperty(ref _highlightedCard, value);
    }

    /// <summary>"£61.00", or "from £61.00".</summary>
    public string TotalText
    {
        get => _totalText;
        private set => SetProperty(ref _totalText, value);
    }

    /// <summary>"65 min", under the total.</summary>
    public string TotalDurationText
    {
        get => _totalDurationText;
        private set => SetProperty(ref _totalDurationText, value);
    }

    /// <summary>The server's word: a time is chosen and every "who with" is answered. False: Continue is grey.</summary>
    public bool CanContinue
    {
        get => _canContinue;
        private set => SetProperty(ref _canContinue, value);
    }

    /// <summary>The server's word: the same, and the salon allows another service. False: "Add another service" is pale.</summary>
    public bool CanAddService
    {
        get => _canAddService;
        private set => SetProperty(ref _canAddService, value);
    }

    /// <summary>Whether to show "Add another service" at all: the salon takes several and the visit is not full.</summary>
    public bool ShowAddService
    {
        get => _showAddService;
        private set => SetProperty(ref _showAddService, value);
    }

    /// <summary>The open staff sheet; null when none is.</summary>
    public StaffSheet? StaffSheet
    {
        get => _staffSheet;
        private set
        {
            if (SetProperty(ref _staffSheet, value))
            {
                OnPropertyChanged(nameof(IsStaffSheetOpen));
            }
        }
    }

    public bool IsStaffSheetOpen => _staffSheet is not null;

    public ICommand LoadCommand { get; }

    public ICommand RetryCommand { get; }

    /// <summary>Parameter: a <see cref="DayModel"/>.</summary>
    public ICommand PickDayCommand { get; }

    public ICommand EarlierCommand { get; }

    public ICommand LaterCommand { get; }

    /// <summary>Parameter: a <see cref="PartOfDayTab"/>. Shows its times; no call.</summary>
    public ICommand SelectPartOfDayCommand { get; }

    /// <summary>Parameter: a <see cref="TimeSlotModel"/>.</summary>
    public ICommand PickTimeCommand { get; }

    /// <summary>Parameter: a <see cref="VisitPartCard"/>. Opens <see cref="StaffSheet"/> for it.</summary>
    public ICommand OpenStaffChoiceCommand { get; }

    /// <summary>Parameter: a <see cref="StaffOption"/> of the open sheet.</summary>
    public ICommand ChooseStaffCommand { get; }

    public ICommand CloseStaffChoiceCommand { get; }

    /// <summary>Parameter: a <see cref="VisitPartCard"/>. The next one moves up; taking out the last goes back to the services.</summary>
    public ICommand RemovePartCommand { get; }

    public ICommand AddServiceCommand { get; }

    public ICommand ContinueCommand { get; }

    /// <summary>After the view's "Discard booking?" (<see cref="TextKeys.TimeDiscardTitle"/>): drops the visit, back to the services.</summary>
    public ICommand LeaveCommand { get; }

    public ICommand DismissMessageCommand { get; }

    /// <summary>Call when the screen appears: shows a message left for it and asks for the visit as chosen.</summary>
    public async Task LoadAsync()
    {
        StaffSheet = null;
        ClearHighlight();
        Message = _session.TakeNotice();

        if (_session.Parts.Count == 0)
        {
            await _navigator.ShowServicesAsync().ConfigureAwait(true);
            return;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    public async Task PickDayAsync(DayModel day)
    {
        ArgumentNullException.ThrowIfNull(day);
        BeginTap();
        _session.Date = day.Date;
        _session.Start = null;
        _selectedPartOfDay = null;
        await RefreshAsync().ConfigureAwait(true);
    }

    public async Task EarlierAsync()
    {
        if (_answer?.EarlierFrom is not { } from)
        {
            return;
        }

        await MoveWindowAsync(from).ConfigureAwait(true);
    }

    public async Task LaterAsync()
    {
        if (_answer?.LaterFrom is not { } from)
        {
            return;
        }

        await MoveWindowAsync(from).ConfigureAwait(true);
    }

    public void SelectPartOfDay(PartOfDayTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _selectedPartOfDay = tab.Key;
        if (_answer is not null)
        {
            BuildTimes(_answer);
        }
    }

    public async Task PickTimeAsync(TimeSlotModel slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        BeginTap();
        _session.Start = slot.Start;
        _selectedPartOfDay = slot.PartOfDay;
        await RefreshAsync().ConfigureAwait(true);
    }

    public void OpenStaffChoice(VisitPartCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        BeginTap();
        StaffSheet = BuildSheet(card.ServiceId);
    }

    public async Task ChooseStaffAsync(StaffOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        var index = IndexOf(option.ServiceId);
        StaffSheet = null;
        if (index < 0)
        {
            return;
        }

        BeginTap();
        _session.SetStaff(index, option.Value);
        await RefreshAsync().ConfigureAwait(true);
    }

    public void CloseStaffChoice()
    {
        StaffSheet = null;
    }

    public async Task RemovePartAsync(VisitPartCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var index = IndexOf(card.ServiceId);
        if (index < 0)
        {
            return;
        }

        BeginTap();
        StaffSheet = null;
        _session.RemovePartAt(index);
        Cards = _cards.Where(c => c.ServiceId != card.ServiceId).ToList();

        if (_session.Parts.Count == 0)
        {
            CancelInFlight();
            _session.Reset();
            Clear();
            await _navigator.ShowServicesAsync().ConfigureAwait(true);
            return;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    public async Task AddServiceAsync()
    {
        if (IsBusy || _answer is null)
        {
            return;
        }

        if (!_answer.CanAddService || !_session.CanHoldAnotherService)
        {
            ExplainMissing(_answer, adding: true);
            return;
        }

        StaffSheet = null;
        _session.Availability = _answer;
        _session.IsAdding = true;
        await _navigator.ShowServicesAsync().ConfigureAwait(true);
    }

    public async Task ContinueAsync()
    {
        // While a tap is on its way the answer on screen is the one before it: wait for the new one.
        if (IsBusy || _answer is null)
        {
            return;
        }

        if (!_answer.CanContinue)
        {
            ExplainMissing(_answer, adding: false);
            return;
        }

        StaffSheet = null;
        _session.Availability = _answer;
        _session.Start = _answer.ChosenStart;
        await _navigator.ShowDetailsAsync().ConfigureAwait(true);
    }

    public async Task LeaveAsync()
    {
        CancelInFlight();
        _session.Reset();
        Clear();
        await _navigator.ShowServicesAsync().ConfigureAwait(true);
    }

    public void Dispose()
    {
        CancelInFlight();
    }

    /// <summary>Asks again for the visit as chosen; the answer replaces the screen unless a later call took over.</summary>
    public async Task RefreshAsync()
    {
        var call = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_gate)
        {
            previous = _inFlight;
            _inFlight = call;
        }

        CancelQuietly(previous);

        var request = new AvailabilityRequest(_session.Parts.ToList(), _session.From, _session.Date, _session.Start);
        IsBusy = true;
        ApiResult<Availability> result;
        try
        {
            result = await _api.GetAvailabilityAsync(request, call.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (call.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_inFlight, call))
                {
                    _inFlight = null;
                }
            }

            call.Dispose();
        }

        if (call.IsCancellationRequested)
        {
            // A later tap took over while this answer was on its way.
            return;
        }

        IsBusy = false;
        if (!result.IsOk)
        {
            var failure = ScreenText.Failure(Text, result);
            if (_answer is null)
            {
                ErrorMessage = failure;
            }
            else
            {
                Message = failure;
            }

            return;
        }

        ErrorMessage = null;
        Apply(request, result.Value);
    }

    private async Task MoveWindowAsync(string from)
    {
        BeginTap();
        _session.From = from;
        _session.Date = null;
        _session.Start = null;
        _selectedPartOfDay = null;
        await RefreshAsync().ConfigureAwait(true);
    }

    private void Apply(AvailabilityRequest request, Availability answer)
    {
        _answer = answer;

        // The client's choices stay; a tapped time the server no longer has drops out quietly.
        if (answer.ChosenStart is not null)
        {
            _session.Start = answer.ChosenStart;
        }
        else if (request.Start is not null)
        {
            _session.Start = null;
        }

        _session.Date = answer.SelectedDate;

        foreach (var person in answer.Parts.SelectMany(p => p.Staff))
        {
            _staffSeen[person.Id] = person;
        }

        MonthTitle = DisplayFormats.MonthTitle(answer.SelectedDate, Text.Culture);
        Days = answer.Days.Select(d => Day(d, answer.SelectedDate)).ToList();
        CanGoEarlier = answer.EarlierFrom is not null;
        CanGoLater = answer.LaterFrom is not null;
        BuildTimes(answer);
        Cards = answer.Parts.Select((p, i) => Card(p, i)).ToList();
        HighlightedCard = null;
        ChosenTimeText = ChosenTime(answer);
        TotalText = DisplayFormats.Price(Text, answer.Total, answer.Currency, answer.TotalIsFrom);
        TotalDurationText = DisplayFormats.Minutes(Text, answer.TotalMinutes);
        CanContinue = answer.CanContinue;
        CanAddService = answer.CanAddService;
        ShowAddService = _session.CanHoldAnotherService;

        if (_staffSheet is not null)
        {
            StaffSheet = BuildSheet(_staffSheet.ServiceId);
        }

        OnPropertyChanged(nameof(HasAnswer));
    }

    private DayModel Day(DayItem day, string selectedDate)
    {
        var date = DisplayFormats.ParseDate(day.Date);
        var weekday = date is null ? string.Empty : DisplayFormats.Weekday(date.Value, Text.Culture);
        var number = date is null ? day.Date : date.Value.Day.ToString(Text.Culture);
        return new DayModel(day.Date, weekday, number, day.HasTimes, string.Equals(day.Date, selectedDate, StringComparison.Ordinal));
    }

    private void BuildTimes(Availability answer)
    {
        var keys = PartsOfDayOrder
            .Where(k => answer.Times.Any(t => string.Equals(t.PartOfDay, k, StringComparison.OrdinalIgnoreCase)))
            .Concat(answer.Times.Select(t => t.PartOfDay).Where(k => !PartsOfDayOrder.Contains(k, StringComparer.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var chosenPart = answer.Times.FirstOrDefault(t => string.Equals(t.Start, answer.ChosenStart, StringComparison.Ordinal))?.PartOfDay;
        var selected = keys.FirstOrDefault(k => string.Equals(k, _selectedPartOfDay, StringComparison.OrdinalIgnoreCase))
            ?? keys.FirstOrDefault(k => string.Equals(k, chosenPart, StringComparison.OrdinalIgnoreCase))
            ?? keys.FirstOrDefault();

        var tabs = keys
            .Select(k => new PartOfDayTab(
                k,
                PartOfDayLabel(k),
                string.Equals(k, selected, StringComparison.OrdinalIgnoreCase),
                answer.Times
                    .Where(t => string.Equals(t.PartOfDay, k, StringComparison.OrdinalIgnoreCase))
                    .Select(t => new TimeSlotModel(t.Start, t.LocalTime, t.PartOfDay, string.Equals(t.Start, answer.ChosenStart, StringComparison.Ordinal)))
                    .ToList()))
            .ToList();

        PartsOfDay = tabs;
        Times = tabs.FirstOrDefault(t => t.IsSelected)?.Times ?? [];

        if (answer.Times.Count > 0)
        {
            NoTimesText = null;
        }
        else
        {
            NoTimesText = Text[answer.Days.Any(d => d.HasTimes) ? TextKeys.TimeNoTimesOnDay : TextKeys.TimeNoTimesInWindow];
        }
    }

    private string PartOfDayLabel(string key)
    {
        return key.ToUpperInvariant() switch
        {
            "MORNING" => Text[TextKeys.TimeMorning],
            "AFTERNOON" => Text[TextKeys.TimeAfternoon],
            "EVENING" => Text[TextKeys.TimeEvening],
            _ => key,
        };
    }

    private VisitPartCard Card(VisitPartItem part, int index)
    {
        var currency = _answer?.Currency ?? _session.Currency;
        string staffText;
        Uri? photo = null;
        if (part.StaffId is { } staffId)
        {
            var person = part.Staff.FirstOrDefault(s => s.Id == staffId) ?? _staffSeen.GetValueOrDefault(staffId);
            staffText = person?.Name ?? Text[TextKeys.TimeNoPreference];
            photo = _session.ResolveUrl(person?.PhotoUrl);
        }
        else if (part.NeedsStaffChoice)
        {
            staffText = Text[TextKeys.TimeNotChosen];
        }
        else
        {
            staffText = Text[TextKeys.TimeNoPreference];
        }

        string? time = null;
        if (part.LocalStart is not null)
        {
            time = part.LocalEnd is null ? part.LocalStart : DisplayFormats.TimeRange(Text, part.LocalStart, part.LocalEnd);
        }

        return new VisitPartCard(
            index,
            part.ServiceId,
            part.Name,
            DisplayFormats.Price(Text, part.Price, currency, part.PriceIsFrom),
            DisplayFormats.Minutes(Text, part.DurationMinutes),
            time,
            staffText,
            photo,
            part.NeedsStaffChoice,
            part.Staff.Count > 0,
            Text[part.NeedsStaffChoice ? TextKeys.TimeChoose : TextKeys.TimeChange]);
    }

    private string? ChosenTime(Availability answer)
    {
        if (answer.ChosenStart is null)
        {
            return null;
        }

        var first = (answer.Parts.Count > 0 ? answer.Parts[0].LocalStart : null)
            ?? answer.Times.FirstOrDefault(t => string.Equals(t.Start, answer.ChosenStart, StringComparison.Ordinal))?.LocalTime;
        var last = answer.Parts.Count > 0 ? answer.Parts[^1].LocalEnd : null;
        if (first is null)
        {
            return null;
        }

        return last is null ? first : DisplayFormats.TimeRange(Text, first, last);
    }

    private StaffSheet? BuildSheet(Guid serviceId)
    {
        var index = IndexOf(serviceId);
        if (_answer is null || index < 0 || index >= _answer.Parts.Count)
        {
            return null;
        }

        var part = _answer.Parts[index];
        var answered = _session.Parts[index].Staff;
        var anyoneChosen = string.Equals(answered, BookingApi.AnyStaff, StringComparison.OrdinalIgnoreCase)
            || (answered is null && part.Anyone && !part.NeedsStaffChoice && part.StaffId is null);

        var options = new List<StaffOption> { new(serviceId, BookingApi.AnyStaff, Text[TextKeys.TimeAnyone], null, true, anyoneChosen) };
        options.AddRange(part.Staff.Select(s => new StaffOption(
            serviceId,
            s.Id.ToString(),
            s.Name,
            _session.ResolveUrl(s.PhotoUrl),
            false,
            part.StaffId == s.Id)));

        var note = _answer.ChosenStart is not null && part.LocalStart is not null ? Text.Format(TextKeys.TimeStaffAtTime, part.LocalStart) : null;
        return new StaffSheet(serviceId, Text.Format(TextKeys.TimeStaffFor, part.Name), note, options);
    }

    private void ExplainMissing(Availability answer, bool adding)
    {
        var timeMissing = answer.ChosenStart is null;
        var owed = _cards.Where(c => c.StaffRequired).ToList();
        var staffMissing = owed.Count > 0;

        Message = (timeMissing, staffMissing) switch
        {
            (true, true) => Text[TextKeys.TimeMissingTimeAndStaff],
            (true, false) => Text[TextKeys.TimeMissingTime],
            (false, true) => Text[TextKeys.TimeMissingStaff],
            _ => Text[adding ? TextKeys.TimeCannotAddMore : TextKeys.TimeCannotContinue],
        };

        foreach (var card in _cards)
        {
            card.IsHighlighted = staffMissing && card.StaffRequired;
        }

        HighlightedCard = owed.FirstOrDefault();
    }

    private int IndexOf(Guid serviceId)
    {
        var parts = _session.Parts;
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i].ServiceId == serviceId)
            {
                return i;
            }
        }

        return -1;
    }

    private void BeginTap()
    {
        Message = null;
        ClearHighlight();
    }

    private void DismissMessage()
    {
        Message = null;
        ClearHighlight();
    }

    private void ClearHighlight()
    {
        foreach (var card in _cards)
        {
            card.IsHighlighted = false;
        }

        HighlightedCard = null;
    }

    private void Clear()
    {
        _answer = null;
        _selectedPartOfDay = null;
        _staffSeen.Clear();
        IsBusy = false;
        ErrorMessage = null;
        Message = null;
        MonthTitle = string.Empty;
        Days = [];
        CanGoEarlier = false;
        CanGoLater = false;
        PartsOfDay = [];
        Times = [];
        NoTimesText = null;
        ChosenTimeText = null;
        Cards = [];
        HighlightedCard = null;
        TotalText = string.Empty;
        TotalDurationText = string.Empty;
        CanContinue = false;
        CanAddService = false;
        ShowAddService = false;
        StaffSheet = null;
        OnPropertyChanged(nameof(HasAnswer));
    }

    private void CancelInFlight()
    {
        CancellationTokenSource? previous;
        lock (_gate)
        {
            previous = _inFlight;
            _inFlight = null;
        }

        CancelQuietly(previous);
    }

    private static void CancelQuietly(CancellationTokenSource? call)
    {
        try
        {
            call?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It finished and cleaned up in the meantime: nothing left to cancel.
        }
    }
}