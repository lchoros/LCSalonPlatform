using System.Windows.Input;
using Salon.Mobile.Core.Api;
using Salon.Mobile.Core.Booking;
using Salon.Mobile.Core.Localization;
using Salon.Mobile.Core.Mvvm;
using Salon.Mobile.Core.Navigation;

namespace Salon.Mobile.Core.Screens.Done;

/// <summary>
/// The confirmation (BOOKING-API step 6): what was booked, when, who with, the price, and "Manage this booking",
/// which opens the visit's own page in the browser (moving and cancelling are done there, not in the app).
/// </summary>
public sealed class DoneScreen : ObservableObject
{
    private readonly IBookingApi _api;
    private readonly INavigator _navigator;
    private readonly BookingSession _session;

    private bool _isBusy;
    private string? _errorMessage;
    private bool _hasVisit;
    private string _serviceName = string.Empty;
    private string _staffName = string.Empty;
    private string _whenText = string.Empty;
    private string _priceText = string.Empty;
    private string _status = string.Empty;
    private Uri? _manageUrl;

    public DoneScreen(IBookingApi api, INavigator navigator, BookingSession session, IAppText text)
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
        RetryCommand = new AsyncCommand(LoadAsync);
        ManageCommand = new AsyncCommand(ManageAsync);
        BookAnotherCommand = new AsyncCommand(BookAnotherAsync);
    }

    /// <summary>The app's words: bind labels as <c>Text[key]</c> (<see cref="TextKeys"/>).</summary>
    public IAppText Text { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>The confirmation could not be loaded (the visit is booked all the same), with <see cref="RetryCommand"/>.</summary>
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

    public bool HasVisit
    {
        get => _hasVisit;
        private set => SetProperty(ref _hasVisit, value);
    }

    /// <summary>Every service of the visit, as the server joined them ("Cut + Colour").</summary>
    public string ServiceName
    {
        get => _serviceName;
        private set => SetProperty(ref _serviceName, value);
    }

    public string StaffName
    {
        get => _staffName;
        private set => SetProperty(ref _staffName, value);
    }

    /// <summary>"Thursday 26 November 2026, 09:45", the salon's day and clock as sent.</summary>
    public string WhenText
    {
        get => _whenText;
        private set => SetProperty(ref _whenText, value);
    }

    public string PriceText
    {
        get => _priceText;
        private set => SetProperty(ref _priceText, value);
    }

    /// <summary>The server's status word for the visit, as sent.</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>The client's own page for the visit on the salon's host.</summary>
    public Uri? ManageUrl
    {
        get => _manageUrl;
        private set => SetProperty(ref _manageUrl, value);
    }

    public ICommand LoadCommand { get; }

    public ICommand RetryCommand { get; }

    public ICommand ManageCommand { get; }

    /// <summary>Starts again from the service list; the salon and its services stay loaded.</summary>
    public ICommand BookAnotherCommand { get; }

    /// <summary>Call when the screen appears.</summary>
    public async Task LoadAsync()
    {
        if (_session.AppointmentId is not { } appointmentId)
        {
            HasVisit = false;
            ErrorMessage = Text[TextKeys.Unexpected];
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        var result = await _api.GetVisitAsync(appointmentId).ConfigureAwait(true);
        IsBusy = false;

        if (!result.IsOk)
        {
            ErrorMessage = ScreenText.Failure(Text, result);
            return;
        }

        var visit = result.Value;
        ServiceName = visit.ServiceName;
        StaffName = visit.StaffName;
        WhenText = Text.Format(TextKeys.DateAndTime, DisplayFormats.LongDate(visit.LocalDate, Text.Culture), visit.LocalTime);
        PriceText = DisplayFormats.Money(visit.Price, visit.Currency, Text.Culture);
        Status = visit.Status;
        ManageUrl = _session.ResolveUrl(visit.ManageUrl);
        HasVisit = true;
    }

    public async Task ManageAsync()
    {
        if (ManageUrl is { } address)
        {
            await _navigator.OpenInBrowserAsync(address).ConfigureAwait(true);
        }
    }

    public async Task BookAnotherAsync()
    {
        _session.Reset();
        HasVisit = false;
        ManageUrl = null;
        await _navigator.ShowServicesAsync().ConfigureAwait(true);
    }
}