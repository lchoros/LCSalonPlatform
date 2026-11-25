using System.Windows.Input;
using Salon.Api.Contracts.Booking;
using Salon.Mobile.Screens.Contracts;

namespace Salon.Mobile.Design;

/// <summary>The service list over <see cref="SampleData.Services"/>: search and filters on the phone, options grouped under one row.</summary>
internal sealed class SampleServicesScreen : Observable, IServicesScreen
{
    private readonly SampleBooking _booking;
    private readonly INavigator _navigator;
    private string _searchText = string.Empty;
    private IReadOnlyList<IServiceRow> _rows = [];
    private IServiceRow? _reading;
    private bool _isAdding;
    private SampleFilter _filter;

    public SampleServicesScreen(SampleBooking booking, INavigator navigator)
    {
        _booking = booking;
        _navigator = navigator;

        var services = SampleData.Services.Services;
        var filters = new List<SampleFilter> { new(ServiceFilterKind.All, null) { IsSelected = true } };
        if (services.Any(service => service.IsPopular))
        {
            filters.Add(new SampleFilter(ServiceFilterKind.Popular, null));
        }

        filters.AddRange(services.Select(service => service.CategoryName).OfType<string>().Distinct().Select(name => new SampleFilter(ServiceFilterKind.Category, name)));
        Filters = filters;
        _filter = filters[0];

        ChooseFilterCommand = new Command<SampleFilter>(filter =>
        {
            _filter.IsSelected = false;
            _filter = filter;
            _filter.IsSelected = true;
            Refresh();
        });
        ShowAllCommand = new Command(() =>
        {
            ChooseFilterCommand.Execute(Filters[0]);
            SearchText = string.Empty;
        });
        ToggleOptionsCommand = new Command<SampleServiceRow>(row => row.IsOpen = !row.IsOpen);
        ReadMoreCommand = new Command<SampleServiceRow>(row => Reading = row);
        CloseReadingCommand = new Command(() => Reading = null);
        BookCommand = new Command<object>(chosen => _ = BookAsync(chosen));
        BackCommand = new Command(async () => await _navigator.GoToAsync(AppScreen.Time));
    }

    public bool IsBusy => false;

    public string? Problem => null;

    public bool IsAdding
    {
        get => _isAdding;
        private set => Set(ref _isAdding, value);
    }

    public bool HasNoServices => SampleData.Services.Services.Count == 0;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? string.Empty))
            {
                Refresh();
            }
        }
    }

    public IReadOnlyList<IServiceFilter> Filters { get; }

    public IReadOnlyList<IServiceRow> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    public IServiceRow? Reading
    {
        get => _reading;
        private set => Set(ref _reading, value);
    }

    public ICommand ChooseFilterCommand { get; }

    public ICommand ShowAllCommand { get; }

    public ICommand ToggleOptionsCommand { get; }

    public ICommand ReadMoreCommand { get; }

    public ICommand CloseReadingCommand { get; }

    public ICommand BookCommand { get; }

    public ICommand BackCommand { get; }

    public Task AppearingAsync()
    {
        IsAdding = _booking.IsAdding;
        Reading = null;
        Refresh();
        return Task.CompletedTask;
    }

    private async Task BookAsync(object chosen)
    {
        var service = chosen switch
        {
            SampleServiceOption option => option.Item,
            SampleServiceRow { HasOptions: false } row => row.Items[0],
            _ => null,
        };
        if (service is null)
        {
            return;
        }

        Reading = null;
        if (IsAdding)
        {
            _booking.Add(service);
        }
        else
        {
            _booking.Start(service);
        }

        await _navigator.GoToAsync(AppScreen.Time);
    }

    private void Refresh()
    {
        var inVisit = IsAdding ? _booking.Parts.Select(part => part.Service.Id).ToHashSet() : [];
        var search = SearchText.Trim();
        Rows = SampleData.Services.Services
            .Where(service => !inVisit.Contains(service.Id))
            .Where(service => _filter.Kind switch
            {
                ServiceFilterKind.Popular => service.IsPopular,
                ServiceFilterKind.Category => service.CategoryName == _filter.CategoryName,
                _ => true,
            })
            .Where(service => search.Length == 0
                || service.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || (service.VariantLabel?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false))
            .GroupBy(service => service.VariantGroup ?? service.Id.ToString())
            .Select(group => (IServiceRow)new SampleServiceRow([.. group]))
            .ToList();
    }
}

internal sealed class SampleFilter(ServiceFilterKind kind, string? categoryName) : Observable, IServiceFilter
{
    private bool _isSelected;

    public ServiceFilterKind Kind { get; } = kind;

    public string? CategoryName { get; } = categoryName;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

/// <summary>A service, or the options of one service (ServiceItems sharing a VariantGroup).</summary>
internal sealed class SampleServiceRow : Observable, IServiceRow
{
    private bool _isOpen;

    public SampleServiceRow(IReadOnlyList<ServiceItem> items)
    {
        Items = items;
        var first = items[0];
        Name = first.VariantGroup ?? first.Name;
        Description = items.Select(item => item.Description).FirstOrDefault(text => text is not null);
        DurationMinutes = items.Min(item => item.DurationMinutes);
        Price = SampleData.Price(items.Min(item => item.Price), items.Count > 1 || first.PriceIsFrom);
        Options = items.Count > 1
            ? [.. items.Select(item => new SampleServiceOption(item))]
            : [];
    }

    public IReadOnlyList<ServiceItem> Items { get; }

    public string Name { get; }

    public string? Description { get; }

    public int DurationMinutes { get; }

    public IPriced Price { get; }

    public IReadOnlyList<IServiceOption> Options { get; }

    public bool HasOptions => Options.Count > 0;

    public bool IsOpen
    {
        get => _isOpen;
        set => Set(ref _isOpen, value);
    }
}

internal sealed class SampleServiceOption(ServiceItem item) : IServiceOption
{
    public ServiceItem Item { get; } = item;

    public string Label => Item.VariantLabel ?? Item.Name;

    public int DurationMinutes => Item.DurationMinutes;

    public IPriced Price { get; } = SampleData.Price(item.Price, item.PriceIsFrom);
}