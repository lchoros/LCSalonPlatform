using System.Windows.Input;
using Salon.Api.Contracts.Booking;
using Salon.Mobile.Core.Api;
using Salon.Mobile.Core.Booking;
using Salon.Mobile.Core.Localization;
using Salon.Mobile.Core.Mvvm;
using Salon.Mobile.Core.Navigation;

namespace Salon.Mobile.Core.Screens.Details;

/// <summary>
/// The details screen (BOOKING-API step 4): the salon's terms, then first name, last name, mobile number, email
/// (optional) and the marketing tick in the server's own words. The app checks only that the required fields are not
/// empty; the server judges the formats and its field messages land under each field. Its answer leads to the code
/// screen, or straight to done where the salon asks for no code. The details are kept on this screen only and never
/// logged.
/// </summary>
public sealed class DetailsScreen : ObservableObject
{
    private const string FirstNameField = "FirstName";
    private const string LastNameField = "LastName";
    private const string PhoneField = "Phone";
    private const string EmailField = "Email";

    private readonly IBookingApi _api;
    private readonly INavigator _navigator;
    private readonly BookingSession _session;

    private bool _isBusy;
    private string? _errorMessage;
    private string? _message;
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;
    private string _phone = string.Empty;
    private string _email = string.Empty;
    private bool _marketingConsent;
    private string? _firstNameError;
    private string? _lastNameError;
    private string? _phoneError;
    private string? _emailError;
    private BookingTerms? _terms;
    private string _servicesText = string.Empty;
    private string? _whenText;
    private string _totalText = string.Empty;

    public DetailsScreen(IBookingApi api, INavigator navigator, BookingSession session, IAppText text)
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
        SubmitCommand = new AsyncCommand(SubmitAsync);
        OpenPrivacyCommand = new AsyncCommand(OpenPrivacyAsync);
        DismissMessageCommand = new RelayCommand(() => Message = null);
    }

    /// <summary>The app's words: bind labels as <c>Text[key]</c> (<see cref="TextKeys"/>).</summary>
    public IAppText Text { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>The terms could not be loaded: the form waits, with <see cref="RetryCommand"/>.</summary>
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

    /// <summary>A message over the form: offline, too many codes, or the server's sentence for what has no field.</summary>
    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string FirstName
    {
        get => _firstName;
        set
        {
            if (SetProperty(ref _firstName, value ?? string.Empty))
            {
                FirstNameError = null;
            }
        }
    }

    public string LastName
    {
        get => _lastName;
        set
        {
            if (SetProperty(ref _lastName, value ?? string.Empty))
            {
                LastNameError = null;
            }
        }
    }

    public string Phone
    {
        get => _phone;
        set
        {
            if (SetProperty(ref _phone, value ?? string.Empty))
            {
                PhoneError = null;
            }
        }
    }

    public string Email
    {
        get => _email;
        set
        {
            if (SetProperty(ref _email, value ?? string.Empty))
            {
                EmailError = null;
            }
        }
    }

    /// <summary>The marketing tick; never ticked for the client.</summary>
    public bool MarketingConsent
    {
        get => _marketingConsent;
        set => SetProperty(ref _marketingConsent, value);
    }

    public string? FirstNameError
    {
        get => _firstNameError;
        private set => SetProperty(ref _firstNameError, value);
    }

    public string? LastNameError
    {
        get => _lastNameError;
        private set => SetProperty(ref _lastNameError, value);
    }

    public string? PhoneError
    {
        get => _phoneError;
        private set => SetProperty(ref _phoneError, value);
    }

    public string? EmailError
    {
        get => _emailError;
        private set => SetProperty(ref _emailError, value);
    }

    /// <summary>The exact words beside the marketing tick, as the server sent them (recorded with a yes).</summary>
    public string ConsentWording => _terms?.ConsentWording ?? string.Empty;

    public Uri? PrivacyUrl => _session.ResolveUrl(_terms?.PrivacyUrl);

    public bool RequiresSmsCode => _terms?.RequiresSmsCode ?? true;

    /// <summary>"Send code" where a code is texted, "Book" where the salon asks for none.</summary>
    public string SubmitText => Text[RequiresSmsCode ? TextKeys.DetailsSendCode : TextKeys.DetailsBook];

    /// <summary>The line under the phone field where a code will be texted; null otherwise.</summary>
    public string? PhoneHint => RequiresSmsCode ? Text[TextKeys.DetailsPhoneHint] : null;

    /// <summary>The cancellation terms, one sentence each, under <see cref="TextKeys.DetailsTermsTitle"/>.</summary>
    public IReadOnlyList<string> TermsLines
    {
        get
        {
            if (_terms is null)
            {
                return [];
            }

            var lines = new List<string>
            {
                _terms.FreeChangeHours == 1 ? Text[TextKeys.DetailsFreeUntilOne] : Text.Format(TextKeys.DetailsFreeUntilMany, _terms.FreeChangeHours),
            };
            if (_terms.LateCancellationFeePercent > 0)
            {
                lines.Add(Text.Format(TextKeys.DetailsLateFee, _terms.LateCancellationFeePercent.ToString("0.##", Text.Culture)));
            }

            if (_terms.NoShowFeePercent > 0)
            {
                lines.Add(Text.Format(TextKeys.DetailsNoShowFee, _terms.NoShowFeePercent.ToString("0.##", Text.Culture)));
            }

            return lines;
        }
    }

    /// <summary>The visit being booked: "Cut + Colour".</summary>
    public string ServicesText
    {
        get => _servicesText;
        private set => SetProperty(ref _servicesText, value);
    }

    /// <summary>"Thursday 26 November 2026, 09:45 – 11:00".</summary>
    public string? WhenText
    {
        get => _whenText;
        private set => SetProperty(ref _whenText, value);
    }

    public string TotalText
    {
        get => _totalText;
        private set => SetProperty(ref _totalText, value);
    }

    public ICommand LoadCommand { get; }

    public ICommand RetryCommand { get; }

    public ICommand SubmitCommand { get; }

    public ICommand OpenPrivacyCommand { get; }

    public ICommand DismissMessageCommand { get; }

    /// <summary>Call when the screen appears. Without a chosen time there is nothing to book: back to the time screen.</summary>
    public async Task LoadAsync()
    {
        var visit = _session.Availability;
        if (visit?.ChosenStart is null || _session.Parts.Count == 0)
        {
            await _navigator.ShowTimeAsync().ConfigureAwait(true);
            return;
        }

        Message = null;
        ServicesText = string.Join(" + ", visit.Parts.Select(p => p.Name));
        var range = visit.Parts.Count > 0 && visit.Parts[0].LocalStart is { } first
            ? (visit.Parts[^1].LocalEnd is { } last ? DisplayFormats.TimeRange(Text, first, last) : first)
            : null;
        WhenText = range is null ? null : Text.Format(TextKeys.DateAndTime, DisplayFormats.LongDate(visit.SelectedDate, Text.Culture), range);
        TotalText = DisplayFormats.Price(Text, visit.Total, visit.Currency, visit.TotalIsFrom);

        if (_session.Terms is null)
        {
            IsBusy = true;
            ErrorMessage = null;
            var result = await _api.GetTermsAsync().ConfigureAwait(true);
            IsBusy = false;
            if (!result.IsOk)
            {
                ErrorMessage = ScreenText.Failure(Text, result);
                return;
            }

            _session.Terms = result.Value;
        }

        ErrorMessage = null;
        SetTerms(_session.Terms);
    }

    public async Task SubmitAsync()
    {
        if (IsBusy)
        {
            return;
        }

        Message = null;
        FirstNameError = string.IsNullOrWhiteSpace(_firstName) ? Text[TextKeys.DetailsFirstNameRequired] : null;
        LastNameError = string.IsNullOrWhiteSpace(_lastName) ? Text[TextKeys.DetailsLastNameRequired] : null;
        PhoneError = string.IsNullOrWhiteSpace(_phone) ? Text[TextKeys.DetailsPhoneRequired] : null;
        EmailError = null;
        if (FirstNameError is not null || LastNameError is not null || PhoneError is not null)
        {
            return;
        }

        var start = _session.Availability?.ChosenStart;
        if (start is null || _terms is null)
        {
            await LoadAsync().ConfigureAwait(true);
            return;
        }

        var request = new StartBookingRequest(
            _session.PartsToBook(),
            start,
            _firstName.Trim(),
            _lastName.Trim(),
            _phone.Trim(),
            string.IsNullOrWhiteSpace(_email) ? null : _email.Trim(),
            _marketingConsent);

        IsBusy = true;
        var result = await _api.StartAsync(request).ConfigureAwait(true);
        IsBusy = false;

        if (result.IsOk)
        {
            await OnStartedAsync(result.Value).ConfigureAwait(true);
            return;
        }

        if (result.IsOffline)
        {
            Message = Text[TextKeys.Offline];
            return;
        }

        await OnRefusedAsync(result.Problem!, result.StatusCode).ConfigureAwait(true);
    }

    public async Task OpenPrivacyAsync()
    {
        if (PrivacyUrl is { } address)
        {
            await _navigator.OpenInBrowserAsync(address).ConfigureAwait(true);
        }
    }

    private async Task OnStartedAsync(StartBookingResponse response)
    {
        if (response.VerificationId is { } verificationId)
        {
            _session.CodeSent(verificationId, response.MaskedPhone);
            await _navigator.ShowCodeAsync().ConfigureAwait(true);
            return;
        }

        if (response.AppointmentId is { } appointmentId)
        {
            _session.ClearVerification();
            _session.AppointmentId = appointmentId;
            await _navigator.ShowDoneAsync().ConfigureAwait(true);
            return;
        }

        Message = Text[TextKeys.Unexpected];
    }

    private async Task OnRefusedAsync(ApiProblem problem, int status)
    {
        if (status == 409 || string.Equals(problem.Code, ApiProblemCodes.SlotTaken, StringComparison.Ordinal))
        {
            _session.TimeHasGone();
            _session.PostNotice(Text[TextKeys.SlotGone]);
            await _navigator.ShowTimeAsync().ConfigureAwait(true);
            return;
        }

        if (status == 429 || string.Equals(problem.Code, ApiProblemCodes.TooManyCodes, StringComparison.Ordinal))
        {
            Message = string.IsNullOrWhiteSpace(problem.Message) ? Text[TextKeys.DetailsTooManyCodes] : problem.Message;
            return;
        }

        var unplaced = new List<string>();
        foreach (var (field, messages) in problem.Fields ?? new Dictionary<string, string[]>())
        {
            var words = string.Join(" ", messages ?? []);
            if (string.IsNullOrWhiteSpace(words))
            {
                continue;
            }

            if (Is(field, FirstNameField))
            {
                FirstNameError = words;
            }
            else if (Is(field, LastNameField))
            {
                LastNameError = words;
            }
            else if (Is(field, PhoneField))
            {
                PhoneError = words;
            }
            else if (Is(field, EmailField))
            {
                EmailError = words;
            }
            else
            {
                unplaced.Add(words);
            }
        }

        var placed = FirstNameError is not null || LastNameError is not null || PhoneError is not null || EmailError is not null;
        if (unplaced.Count > 0)
        {
            Message = string.Join(" ", unplaced);
        }
        else if (!placed)
        {
            Message = string.IsNullOrWhiteSpace(problem.Message) ? Text[TextKeys.Unexpected] : problem.Message;
        }
    }

    private static bool Is(string field, string name)
    {
        return string.Equals(field, name, StringComparison.OrdinalIgnoreCase);
    }

    private void SetTerms(BookingTerms terms)
    {
        _terms = terms;
        OnPropertyChanged(nameof(ConsentWording));
        OnPropertyChanged(nameof(PrivacyUrl));
        OnPropertyChanged(nameof(RequiresSmsCode));
        OnPropertyChanged(nameof(SubmitText));
        OnPropertyChanged(nameof(PhoneHint));
        OnPropertyChanged(nameof(TermsLines));
    }
}