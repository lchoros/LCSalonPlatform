using System.Globalization;
using System.Text;
using System.Windows.Input;
using Salon.Api.Contracts.Booking;
using Salon.Mobile.Core.Api;
using Salon.Mobile.Core.Booking;
using Salon.Mobile.Core.Localization;
using Salon.Mobile.Core.Mvvm;
using Salon.Mobile.Core.Navigation;

namespace Salon.Mobile.Core.Screens.Services;

/// <summary>
/// The service list (BOOKING-API step 1): loaded once, then filtered and searched on the phone. Options of one service
/// share a row that opens. In adding mode (<see cref="BookingSession.IsAdding"/>) the services already in the visit
/// are not offered and a choice extends the visit; otherwise a choice starts a new one. Either way the time screen
/// follows.
/// </summary>
public sealed class ServicesScreen : ObservableObject
{
    private readonly IBookingApi _api;
    private readonly INavigator _navigator;
    private readonly BookingSession _session;
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);

    private bool _isBusy;
    private string? _errorMessage;
    private string? _message;
    private string _searchText = string.Empty;
    private string _selectedChipKey = FilterChip.AllKey;
    private bool _isAdding;
    private IReadOnlyList<FilterChip> _chips = [];
    private IReadOnlyList<ServiceRow> _rows = [];

    public ServicesScreen(IBookingApi api, INavigator navigator, BookingSession session, IAppText text)
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
        RetryCommand = new AsyncCommand(RetryAsync);
        SelectChipCommand = new RelayCommand<FilterChip>(SelectChip);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        TapRowCommand = new AsyncCommand<ServiceRow>(TapRowAsync);
        ChooseServiceCommand = new AsyncCommand<ServiceOption>(ChooseServiceAsync);
        BackToBookingCommand = new AsyncCommand(BackToBookingAsync);
        DismissMessageCommand = new RelayCommand(() => Message = null);
    }

    /// <summary>The app's words: bind labels as <c>Text[key]</c> (<see cref="TextKeys"/>).</summary>
    public IAppText Text { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Why the list could not be loaded (offline, refused); with <see cref="RetryCommand"/>.</summary>
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

    /// <summary>A passing message over the list (the visit is full).</summary>
    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string SalonName => _session.Salon?.Name ?? string.Empty;

    public bool IsAdding
    {
        get => _isAdding;
        private set
        {
            if (SetProperty(ref _isAdding, value))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Lead));
            }
        }
    }

    /// <summary>"Choose a service", or "Add another service" in adding mode.</summary>
    public string Title => Text[_isAdding ? TextKeys.ServicesAddTitle : TextKeys.ServicesTitle];

    /// <summary>The line under the title in adding mode; null otherwise.</summary>
    public string? Lead => _isAdding ? Text[TextKeys.ServicesAddLead] : null;

    /// <summary>The search box; every change filters the list at once.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                Rebuild();
            }
        }
    }

    /// <summary>All, then Popular when any service is ticked for it, then each category in the salon's order.</summary>
    public IReadOnlyList<FilterChip> Chips
    {
        get => _chips;
        private set => SetProperty(ref _chips, value);
    }

    public IReadOnlyList<ServiceRow> Rows
    {
        get => _rows;
        private set
        {
            if (SetProperty(ref _rows, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(HasNoMatch));
            }
        }
    }

    /// <summary>The salon offers nothing to book (online): show <see cref="TextKeys.ServicesNone"/>.</summary>
    public bool IsEmpty => _session.HasServices && OfferedServices().Count == 0;

    /// <summary>There are services, but none passes the search and the chip: show <see cref="TextKeys.ServicesNoMatch"/> and "Show all".</summary>
    public bool HasNoMatch => _rows.Count == 0 && _session.HasServices && !IsEmpty;

    public ICommand LoadCommand { get; }

    public ICommand RetryCommand { get; }

    /// <summary>Parameter: a <see cref="FilterChip"/>.</summary>
    public ICommand SelectChipCommand { get; }

    /// <summary>Back to All with an empty search.</summary>
    public ICommand ClearFiltersCommand { get; }

    /// <summary>Parameter: a <see cref="ServiceRow"/>. A group opens or closes; a single service is chosen.</summary>
    public ICommand TapRowCommand { get; }

    /// <summary>Parameter: a <see cref="ServiceOption"/>, a row's <see cref="ServiceRow.OnlyOption"/> or one of a group's options.</summary>
    public ICommand ChooseServiceCommand { get; }

    /// <summary>In adding mode: back to the time screen with the visit as it was.</summary>
    public ICommand BackToBookingCommand { get; }

    public ICommand DismissMessageCommand { get; }

    /// <summary>Call when the screen appears. Loads the list the first time only; the mode is read again every time.</summary>
    public async Task LoadAsync()
    {
        IsAdding = _session.IsAdding && _session.Parts.Count > 0;
        Message = null;

        if (!_session.HasServices)
        {
            IsBusy = true;
            ErrorMessage = null;
            var result = await _api.GetServicesAsync().ConfigureAwait(true);
            IsBusy = false;

            if (!result.IsOk)
            {
                ErrorMessage = ScreenText.Failure(Text, result);
                return;
            }

            _session.SetServices(result.Value);
            Text.UseCulture(result.Value.Salon.Culture);
            OnPropertyChanged(nameof(SalonName));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Lead));
        }

        ErrorMessage = null;
        BuildChips();
        Rebuild();
    }

    public Task RetryAsync()
    {
        return LoadAsync();
    }

    public void SelectChip(FilterChip chip)
    {
        ArgumentNullException.ThrowIfNull(chip);
        _selectedChipKey = chip.Key;
        foreach (var each in _chips)
        {
            each.IsSelected = string.Equals(each.Key, chip.Key, StringComparison.Ordinal);
        }

        Rebuild();
    }

    public void ClearFilters()
    {
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        var all = _chips.FirstOrDefault(c => string.Equals(c.Key, FilterChip.AllKey, StringComparison.Ordinal));
        if (all is not null)
        {
            SelectChip(all);
        }
        else
        {
            Rebuild();
        }
    }

    public async Task TapRowAsync(ServiceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.OnlyOption is { } only)
        {
            await ChooseServiceAsync(only).ConfigureAwait(true);
            return;
        }

        row.IsExpanded = !row.IsExpanded;
        if (row.IsExpanded)
        {
            _expanded.Add(row.Key);
        }
        else
        {
            _expanded.Remove(row.Key);
        }
    }

    public async Task ChooseServiceAsync(ServiceOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (IsAdding)
        {
            if (!_session.AddService(option.ServiceId))
            {
                Message = Text[TextKeys.ServicesFull];
                return;
            }
        }
        else
        {
            _session.StartVisit(option.ServiceId);
        }

        IsAdding = false;
        await _navigator.ShowTimeAsync().ConfigureAwait(true);
    }

    public async Task BackToBookingAsync()
    {
        _session.IsAdding = false;
        IsAdding = false;
        await _navigator.ShowTimeAsync().ConfigureAwait(true);
    }

    private List<ServiceItem> OfferedServices()
    {
        return IsAdding ? _session.Services.Where(s => !_session.Contains(s.Id)).ToList() : _session.Services.ToList();
    }

    private void BuildChips()
    {
        var services = _session.Services;
        var chips = new List<FilterChip> { new(FilterChip.AllKey, Text[TextKeys.ServicesAll], null, false) };
        if (services.Any(s => s.IsPopular))
        {
            chips.Add(new FilterChip(FilterChip.PopularKey, Text[TextKeys.ServicesPopular], null, false));
        }

        foreach (var category in services.Select(s => s.CategoryName).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal))
        {
            chips.Add(new FilterChip(FilterChip.CategoryPrefix + category, category!, category, false));
        }

        if (!chips.Exists(c => string.Equals(c.Key, _selectedChipKey, StringComparison.Ordinal)))
        {
            _selectedChipKey = FilterChip.AllKey;
        }

        foreach (var chip in chips)
        {
            chip.IsSelected = string.Equals(chip.Key, _selectedChipKey, StringComparison.Ordinal);
        }

        Chips = chips;
    }

    private void Rebuild()
    {
        if (!_session.HasServices)
        {
            Rows = [];
            return;
        }

        var chip = _chips.FirstOrDefault(c => string.Equals(c.Key, _selectedChipKey, StringComparison.Ordinal));
        var matching = OfferedServices()
            .Where(s => PassesChip(s, chip) && MatchesSearch(s, _searchText.Trim()))
            .ToList();

        Rows = Group(matching);
    }

    private static bool PassesChip(ServiceItem service, FilterChip? chip)
    {
        if (chip is null || string.Equals(chip.Key, FilterChip.AllKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(chip.Key, FilterChip.PopularKey, StringComparison.Ordinal))
        {
            return service.IsPopular;
        }

        return string.Equals(service.CategoryName, chip.Category, StringComparison.Ordinal);
    }

    private static bool MatchesSearch(ServiceItem service, string search)
    {
        if (search.Length == 0)
        {
            return true;
        }

        // Case and accents aside: "zel" finds "Żel", "MANI" finds "Manicure". Folded by hand, because Polish collation
        // keeps Ż and Ł letters of their own.
        var folded = Fold(search);
        return new[] { service.Name, service.VariantGroup, service.VariantLabel, service.Description, service.CategoryName }
            .Any(field => !string.IsNullOrEmpty(field) && Fold(field).Contains(folded, StringComparison.Ordinal));
    }

    private static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(c switch
            {
                'ł' or 'Ł' => 'L',
                _ => char.ToUpperInvariant(c),
            });
        }

        return builder.ToString();
    }

    private List<ServiceRow> Group(List<ServiceItem> services)
    {
        var rows = new List<ServiceRow>();
        var groups = new Dictionary<string, List<ServiceItem>>(StringComparer.Ordinal);
        var order = new List<(string? Group, ServiceItem Service)>();

        foreach (var service in services)
        {
            if (string.IsNullOrWhiteSpace(service.VariantGroup))
            {
                order.Add((null, service));
                continue;
            }

            if (!groups.TryGetValue(service.VariantGroup, out var members))
            {
                members = [];
                groups[service.VariantGroup] = members;
                order.Add((service.VariantGroup, service));
            }

            members.Add(service);
        }

        foreach (var (group, service) in order)
        {
            if (group is null || groups[group].Count == 1)
            {
                rows.Add(SingleRow(service));
            }
            else
            {
                rows.Add(GroupRow(group, groups[group]));
            }
        }

        return rows;
    }

    private ServiceRow SingleRow(ServiceItem service)
    {
        var option = Option(service, service.Name);
        return new ServiceRow(service.Id.ToString(), service.Name, service.Description, option.PriceText, option.DurationText, [option], null, false);
    }

    private ServiceRow GroupRow(string group, List<ServiceItem> members)
    {
        var key = "group:" + group;
        var options = members.Select(m => Option(m, string.IsNullOrWhiteSpace(m.VariantLabel) ? m.Name : m.VariantLabel)).ToList();
        var lowest = members.Min(m => m.Price);
        var isFrom = members.Any(m => m.PriceIsFrom) || members.Any(m => m.Price != lowest);
        var price = DisplayFormats.Price(Text, lowest, _session.Currency, isFrom);
        var duration = DisplayFormats.MinutesRange(Text, members.Min(m => m.DurationMinutes), members.Max(m => m.DurationMinutes));
        var summary = Text.Format(TextKeys.ServicesOptions, string.Join(", ", options.Select(o => o.Label)));
        var description = members.Select(m => m.Description).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));
        return new ServiceRow(key, group, description, price, duration, options, summary, _expanded.Contains(key));
    }

    private ServiceOption Option(ServiceItem service, string label)
    {
        return new ServiceOption(
            service.Id,
            service.Name,
            label,
            service.Description,
            DisplayFormats.Price(Text, service.Price, _session.Currency, service.PriceIsFrom),
            DisplayFormats.Minutes(Text, service.DurationMinutes),
            service.IsPopular);
    }
}