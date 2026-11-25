using System.Globalization;
using System.Windows.Input;
using Salon.Api.Contracts.Booking;
using Salon.Mobile.Screens.Contracts;

namespace Salon.Mobile.Design;

/// <summary>
/// The time screen over made-up availability: fourteen days a window, Sundays closed, the evening only on some days.
/// Each tap waits a moment as a real call would, so the faded "on its way" state can be seen.
/// </summary>
internal sealed class SampleTimeScreen : Observable, ITimeScreen
{
    private const int WindowDays = 14;

    private static readonly string[] MorningTimes = ["09:00", "09:15", "09:45", "10:30", "11:00", "11:30"];
    private static readonly string[] AfternoonTimes = ["12:15", "13:00", "13:30", "14:45", "15:30"];
    private static readonly string[] EveningTimes = ["16:00", "16:30", "17:15", "18:00"];

    private readonly SampleBooking _booking;
    private readonly INavigator _navigator;
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly _from;
    private DateOnly _date;
    private PartOfDay _part;
    private bool _isBusy;
    private TimeHint _hint;
    private IStaffSheet? _staffSheet;
    private HashSet<Guid> _flagged = [];

    public SampleTimeScreen(SampleBooking booking, INavigator navigator)
    {
        _booking = booking;
        _navigator = navigator;
        _from = _today;
        _date = FirstDayWithTimes(_from);

        EarlierCommand = new Command(() => Tap(() => Move(-WindowDays)));
        LaterCommand = new Command(() => Tap(() => Move(WindowDays)));
        ChooseDayCommand = new Command<SampleDay>(day => Tap(() =>
        {
            _date = day.Day;
            _booking.Time = null;
            _part = PartsWithTimes(_date).FirstOrDefault();
        }));
        ChoosePartOfDayCommand = new Command<SamplePartOfDay>(tab => Tap(() => _part = tab.Part));
        ChooseTimeCommand = new Command<SampleTime>(time => Tap(() =>
        {
            _booking.Date = Iso(_date);
            _booking.Time = time.LocalTime;
            if (_hint is TimeHint.ChooseTime or TimeHint.ChooseTimeBeforeAdding)
            {
                _hint = TimeHint.None;
            }
            else if (_hint == TimeHint.ChooseTimeAndStaff)
            {
                _hint = TimeHint.ChooseStaff;
            }
        }));
        ChooseStaffCommand = new Command<SampleVisitPart>(part => StaffSheet = new SampleStaffSheet(part.Service, Answer(part.Service.Id), _booking.Time, this));
        RemovePartCommand = new Command<SampleVisitPart>(part => Tap(() => _booking.Remove(part.Service.Id)));
        AddServiceCommand = new Command(async () => await AddServiceAsync());
        ContinueCommand = new Command(async () => await ContinueAsync());
        CloseCommand = new Command(async () => await CloseAsync());
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public string? Problem => null;

    public string SelectedDate => Iso(_date);

    public bool CanGoEarlier => _from > _today;

    public bool CanGoLater => _from < _today.AddDays(WindowDays * 4);

    public IReadOnlyList<IDayTile> Days =>
        [.. Enumerable.Range(0, WindowDays).Select(offset => _from.AddDays(offset)).Select(day => new SampleDay(day, HasTimes(day), day == _date))];

    public IReadOnlyList<IPartOfDayTab> PartsOfDay => [.. PartsWithTimes(_date).Select(part => new SamplePartOfDay(part, part == _part))];

    public IReadOnlyList<ITimeChip> Times =>
        [.. TimesOf(_date, _part).Select(time => new SampleTime(time, _booking.Date == Iso(_date) && _booking.Time == time))];

    public IReadOnlyList<IVisitPart> Parts
    {
        get
        {
            var start = ChosenStart();
            var parts = new List<IVisitPart>();
            foreach (var (service, staff) in _booking.Parts)
            {
                var end = start?.AddMinutes(service.DurationMinutes);
                var people = SampleData.StaffFor(service.Id);
                var person = Guid.TryParse(staff, out var id) ? people.FirstOrDefault(p => p.Id == id) : null;
                var onlyOne = people.Count == 1;
                parts.Add(new SampleVisitPart(
                    service,
                    SampleData.Price(service.Price, service.PriceIsFrom),
                    start?.ToString("HH:mm", CultureInfo.InvariantCulture),
                    end?.ToString("HH:mm", CultureInfo.InvariantCulture),
                    person?.Name,
                    person?.PhotoUrl,
                    Anyone: staff == BookingApi.AnyStaff || onlyOne,
                    NeedsStaffChoice: staff is null && !onlyOne,
                    CanRemove: _booking.Parts.Count > 1,
                    IsFlagged: _flagged.Contains(service.Id)));
                start = end;
            }

            return parts;
        }
    }

    public IPriced Total => SampleData.Price(_booking.Parts.Sum(part => part.Service.Price), _booking.Parts.Any(part => part.Service.PriceIsFrom));

    public int TotalMinutes => _booking.Parts.Sum(part => part.Service.DurationMinutes);

    public bool CanContinue => ChosenStart() is not null && !Parts.Any(part => part.NeedsStaffChoice);

    public bool CanAddService => CanContinue && _booking.Parts.Count < SampleData.Salon.MaxServices;

    public TimeHint Hint
    {
        get => _hint;
        private set => Set(ref _hint, value);
    }

    public IStaffSheet? StaffSheet
    {
        get => _staffSheet;
        private set => Set(ref _staffSheet, value);
    }

    public ICommand EarlierCommand { get; }

    public ICommand LaterCommand { get; }

    public ICommand ChooseDayCommand { get; }

    public ICommand ChoosePartOfDayCommand { get; }

    public ICommand ChooseTimeCommand { get; }

    public ICommand ChooseStaffCommand { get; }

    public ICommand RemovePartCommand { get; }

    public ICommand AddServiceCommand { get; }

    public ICommand ContinueCommand { get; }

    public ICommand CloseCommand { get; }

    public Task AppearingAsync()
    {
        _booking.IsAdding = false;
        if (_booking.Date is { } date && DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            _date = day;
        }

        if (!PartsWithTimes(_date).Contains(_part))
        {
            _part = PartsWithTimes(_date).FirstOrDefault();
        }

        _flagged = [];
        _hint = TimeHint.None;
        _staffSheet = null;
        RaiseAll();
        return Task.CompletedTask;
    }

    /// <summary>The sheet's answer for one service.</summary>
    internal void Choose(ServiceItem service, string staff)
    {
        StaffSheet = null;
        Tap(() =>
        {
            _booking.Answer(service.Id, staff);
            _flagged.Remove(service.Id);
            if (_hint == TimeHint.ChooseStaff && _flagged.Count == 0)
            {
                _hint = TimeHint.None;
            }
        });
    }

    internal void CloseSheet() => StaffSheet = null;

    private string? Answer(Guid serviceId) => _booking.Parts.FirstOrDefault(part => part.Service.Id == serviceId).Staff;

    private async Task AddServiceAsync()
    {
        if (!CanAddService)
        {
            Hint = ChosenStart() is null ? TimeHint.ChooseTimeBeforeAdding : Parts.Any(part => part.NeedsStaffChoice) ? TimeHint.ChooseStaff : TimeHint.VisitFull;
            return;
        }

        _booking.IsAdding = true;
        await _navigator.GoToAsync(AppScreen.AddService);
    }

    private async Task ContinueAsync()
    {
        if (CanContinue)
        {
            await _navigator.GoToAsync(AppScreen.Details);
            return;
        }

        var owed = Parts.OfType<SampleVisitPart>().Where(part => part.NeedsStaffChoice).Select(part => part.Service.Id).ToHashSet();
        var noTime = ChosenStart() is null;
        _flagged = owed;
        Hint = (noTime, owed.Count > 0) switch
        {
            (true, true) => TimeHint.ChooseTimeAndStaff,
            (true, false) => TimeHint.ChooseTime,
            _ => TimeHint.ChooseStaff,
        };
        Raise(nameof(Parts));
    }

    private async Task CloseAsync()
    {
        if (await _navigator.ConfirmDiscardAsync())
        {
            _booking.Reset();
            await _navigator.GoToAsync(AppScreen.Services);
        }
    }

    private async void Tap(Action change)
    {
        IsBusy = true;
        await Task.Delay(250);
        change();
        IsBusy = false;
        RaiseAll();
    }

    private void Move(int days)
    {
        _from = _from.AddDays(days) < _today ? _today : _from.AddDays(days);
        _date = FirstDayWithTimes(_from);
        _part = PartsWithTimes(_date).FirstOrDefault();
    }

    private void RaiseAll()
    {
        foreach (var name in new[] { nameof(SelectedDate), nameof(CanGoEarlier), nameof(CanGoLater), nameof(Days), nameof(PartsOfDay), nameof(Times), nameof(Parts), nameof(Total), nameof(TotalMinutes), nameof(CanContinue), nameof(CanAddService), nameof(Hint), nameof(StaffSheet) })
        {
            Raise(name);
        }
    }

    private TimeOnly? ChosenStart() =>
        _booking.Time is { } time && TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ? start : null;

    private static DateOnly FirstDayWithTimes(DateOnly from) =>
        Enumerable.Range(0, WindowDays).Select(offset => from.AddDays(offset)).FirstOrDefault(HasTimes, from);

    private static bool HasTimes(DateOnly day) => day.DayOfWeek != DayOfWeek.Sunday && day.DayNumber % 9 != 4;

    private static IEnumerable<PartOfDay> PartsWithTimes(DateOnly day)
    {
        if (!HasTimes(day))
        {
            yield break;
        }

        yield return PartOfDay.Morning;
        yield return PartOfDay.Afternoon;
        if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Monday)
        {
            yield return PartOfDay.Evening;
        }
    }

    private static IEnumerable<string> TimesOf(DateOnly day, PartOfDay part) => !PartsWithTimes(day).Contains(part) ? [] : part switch
    {
        PartOfDay.Morning => MorningTimes.Skip(day.Day % 3),
        PartOfDay.Afternoon => AfternoonTimes,
        _ => EveningTimes,
    };

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

internal sealed record SampleDay(DateOnly Day, bool HasTimes, bool IsSelected) : IDayTile
{
    public string LocalDate => Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

internal sealed record SamplePartOfDay(PartOfDay Part, bool IsSelected) : IPartOfDayTab;

internal sealed record SampleTime(string LocalTime, bool IsSelected) : ITimeChip;

internal sealed record SampleVisitPart(
    ServiceItem Service,
    IPriced Price,
    string? LocalStart,
    string? LocalEnd,
    string? StaffName,
    string? StaffPhotoUrl,
    bool Anyone,
    bool NeedsStaffChoice,
    bool CanRemove,
    bool IsFlagged) : IVisitPart
{
    public string Name => Service.VariantLabel is { } label ? $"{Service.Name} ({label})" : Service.Name;

    public int DurationMinutes => Service.DurationMinutes;
}

internal sealed class SampleStaffSheet : IStaffSheet
{
    public SampleStaffSheet(ServiceItem service, string? answer, string? atTime, SampleTimeScreen screen)
    {
        ServiceName = service.VariantLabel is { } label ? $"{service.Name} ({label})" : service.Name;
        AtTime = atTime;
        Choices =
        [
            new SampleStaffChoice(null, answer == BookingApi.AnyStaff),
            .. SampleData.StaffFor(service.Id).Select(person => new SampleStaffChoice(person, answer == person.Id.ToString())),
        ];
        ChooseCommand = new Command<SampleStaffChoice>(choice => screen.Choose(service, choice.Person?.Id.ToString() ?? BookingApi.AnyStaff));
        CloseCommand = new Command(screen.CloseSheet);
    }

    public string ServiceName { get; }

    public string? AtTime { get; }

    public IReadOnlyList<IStaffChoice> Choices { get; }

    public ICommand ChooseCommand { get; }

    public ICommand CloseCommand { get; }
}

internal sealed record SampleStaffChoice(StaffItem? Person, bool IsSelected) : IStaffChoice
{
    public bool IsAnyone => Person is null;

    public string? Name => Person?.Name;

    public string? PhotoUrl => Person?.PhotoUrl;
}