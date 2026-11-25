using System.Windows.Input;
using Salon.Mobile.Configuration;
using Salon.Mobile.Resources.Strings;
using Salon.Mobile.Screens.Contracts;

namespace Salon.Mobile.Design;

/// <summary>The details form with the checks the server would make, so the per-field errors can be seen.</summary>
internal sealed class SampleDetailsScreen : Observable, IDetailsScreen
{
    private readonly SampleBooking _booking;
    private readonly INavigator _navigator;
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;
    private string _phone = string.Empty;
    private string _email = string.Empty;
    private bool _marketingConsent;
    private string? _firstNameError;
    private string? _lastNameError;
    private string? _phoneError;
    private string? _emailError;
    private bool _isBusy;

    public SampleDetailsScreen(SampleBooking booking, INavigator navigator, SalonAppOptions options)
    {
        _booking = booking;
        _navigator = navigator;
        Visit = booking.Summary();
        OpenPrivacyCommand = new Command(async () => await _navigator.OpenBrowserAsync(new Uri(options.BaseAddress, "/privacy")));
        SubmitCommand = new Command(async () => await SubmitAsync());
        BackCommand = new Command(async () => await _navigator.BackAsync());
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public string? Problem => null;

    public IVisitSummary Visit { get; private set; }

    public string FirstName
    {
        get => _firstName;
        set => Set(ref _firstName, value ?? string.Empty);
    }

    public string LastName
    {
        get => _lastName;
        set => Set(ref _lastName, value ?? string.Empty);
    }

    public string Phone
    {
        get => _phone;
        set => Set(ref _phone, value ?? string.Empty);
    }

    public string Email
    {
        get => _email;
        set => Set(ref _email, value ?? string.Empty);
    }

    public bool MarketingConsent
    {
        get => _marketingConsent;
        set => Set(ref _marketingConsent, value);
    }

    public string ConsentWording =>
        $"I agree to {SampleData.Salon.Name} sending me news and offers by SMS and email. I can withdraw this consent at any time.";

    public bool RequiresCode => true;

    public string? FirstNameError
    {
        get => _firstNameError;
        private set => Set(ref _firstNameError, value);
    }

    public string? LastNameError
    {
        get => _lastNameError;
        private set => Set(ref _lastNameError, value);
    }

    public string? PhoneError
    {
        get => _phoneError;
        private set => Set(ref _phoneError, value);
    }

    public string? EmailError
    {
        get => _emailError;
        private set => Set(ref _emailError, value);
    }

    public ICommand OpenPrivacyCommand { get; }

    public ICommand SubmitCommand { get; }

    public ICommand BackCommand { get; }

    public Task AppearingAsync()
    {
        Visit = _booking.Summary();
        Raise(nameof(Visit));
        return Task.CompletedTask;
    }

    private async Task SubmitAsync()
    {
        var digits = Phone.Count(char.IsAsciiDigit);
        FirstNameError = string.IsNullOrWhiteSpace(FirstName) ? AppResources.FirstNameRequired : null;
        LastNameError = string.IsNullOrWhiteSpace(LastName) ? AppResources.LastNameRequired : null;
        PhoneError = string.IsNullOrWhiteSpace(Phone) ? AppResources.PhoneRequired : digits < 9 ? AppResources.PhoneInvalid : null;
        EmailError = Email.Length > 0 && !Email.Contains('@', StringComparison.Ordinal) ? AppResources.EmailInvalid : null;
        if (FirstNameError is not null || LastNameError is not null || PhoneError is not null || EmailError is not null)
        {
            return;
        }

        IsBusy = true;
        await Task.Delay(400);
        IsBusy = false;
        _booking.Phone = Phone;
        await _navigator.GoToAsync(AppScreen.Code);
    }
}

/// <summary>The code screen: 123456 books; anything else costs an attempt.</summary>
internal sealed class SampleCodeScreen : Observable, ICodeScreen
{
    private const string RightCode = "123456";

    private readonly SampleBooking _booking;
    private readonly INavigator _navigator;
    private string _code = string.Empty;
    private int? _attemptsLeft;
    private string? _problem;
    private bool _isBusy;

    public SampleCodeScreen(SampleBooking booking, INavigator navigator)
    {
        _booking = booking;
        _navigator = navigator;
        VerifyCommand = new Command(async () => await VerifyAsync());
        BackCommand = new Command(async () => await _navigator.BackAsync());
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public string? Problem
    {
        get => _problem;
        private set => Set(ref _problem, value);
    }

    public string MaskedPhone
    {
        get
        {
            var digits = new string([.. _booking.Phone.Where(char.IsAsciiDigit)]);
            return digits.Length < 3 ? "•••" : $"••• ••• {digits[^3..]}";
        }
    }

    public string Code
    {
        get => _code;
        set => Set(ref _code, value ?? string.Empty);
    }

    public int? AttemptsLeft
    {
        get => _attemptsLeft;
        private set => Set(ref _attemptsLeft, value);
    }

    public ICommand VerifyCommand { get; }

    public ICommand BackCommand { get; }

    public Task AppearingAsync()
    {
        Code = string.Empty;
        AttemptsLeft = null;
        Problem = null;
        Raise(nameof(MaskedPhone));
        return Task.CompletedTask;
    }

    private async Task VerifyAsync()
    {
        if (Code.Length != 6 || !Code.All(char.IsAsciiDigit))
        {
            Problem = AppResources.CodeInvalid;
            return;
        }

        Problem = null;
        IsBusy = true;
        await Task.Delay(400);
        IsBusy = false;
        if (Code == RightCode)
        {
            await _navigator.GoToAsync(AppScreen.Done);
            return;
        }

        AttemptsLeft = (AttemptsLeft ?? 3) - 1;
        Code = string.Empty;
        if (AttemptsLeft == 0)
        {
            AttemptsLeft = null;
            Problem = AppResources.CodeTooMany;
        }
    }
}

/// <summary>The confirmation of the sample visit.</summary>
internal sealed class SampleDoneScreen : Observable, IDoneScreen
{
    private readonly SampleBooking _booking;

    public SampleDoneScreen(SampleBooking booking, INavigator navigator, SalonAppOptions options)
    {
        _booking = booking;
        Visit = booking.Summary();
        ManageCommand = new Command(async () => await navigator.OpenBrowserAsync(new Uri(options.BaseAddress, "/book/done")));
        BookAnotherCommand = new Command(async () =>
        {
            _booking.Reset();
            await navigator.GoToAsync(AppScreen.Services);
        });
    }

    public bool IsBusy => false;

    public string? Problem => null;

    public IVisitSummary Visit { get; private set; }

    public ICommand ManageCommand { get; }

    public ICommand BookAnotherCommand { get; }

    public Task AppearingAsync()
    {
        Visit = _booking.Summary();
        Raise(nameof(Visit));
        return Task.CompletedTask;
    }
}