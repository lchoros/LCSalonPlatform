using System.Windows.Input;
using Salon.Api.Contracts.Booking;
using Salon.Mobile.Core.Api;
using Salon.Mobile.Core.Booking;
using Salon.Mobile.Core.Localization;
using Salon.Mobile.Core.Mvvm;
using Salon.Mobile.Core.Navigation;

namespace Salon.Mobile.Core.Screens.Code;

/// <summary>
/// The code screen (BOOKING-API step 5): six digits, then verify. A wrong code says how many attempts are left; a time
/// taken meanwhile, an expired code, too many wrong codes or a lost verification go back to the time screen with a
/// message, the visit kept; booked goes to done.
/// </summary>
public sealed class CodeScreen : ObservableObject
{
    public const int CodeLength = 6;

    private readonly IBookingApi _api;
    private readonly INavigator _navigator;
    private readonly BookingSession _session;

    private Guid? _loadedFor;
    private bool _isBusy;
    private string _code = string.Empty;
    private string? _codeError;
    private string? _message;
    private string _sentText = string.Empty;

    public CodeScreen(IBookingApi api, INavigator navigator, BookingSession session, IAppText text)
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
        VerifyCommand = new AsyncCommand(VerifyAsync);
        DismissMessageCommand = new RelayCommand(() => Message = null);
    }

    /// <summary>The app's words: bind labels as <c>Text[key]</c> (<see cref="TextKeys"/>).</summary>
    public IAppText Text { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>The code typed so far; only digits are kept, at most six.</summary>
    public string Code
    {
        get => _code;
        set
        {
            var digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).Take(CodeLength).ToArray());
            if (SetProperty(ref _code, digits))
            {
                CodeError = null;
                OnPropertyChanged(nameof(CanVerify));
            }
        }
    }

    /// <summary>All six digits are in: the view may submit by itself, or light up the button.</summary>
    public bool CanVerify => _code.Length == CodeLength;

    /// <summary>Under the field: the code is not six digits, or a wrong one with the attempts left.</summary>
    public string? CodeError
    {
        get => _codeError;
        private set => SetProperty(ref _codeError, value);
    }

    /// <summary>A message over the screen: offline, too many codes, something unexpected.</summary>
    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>"We've texted a 6-digit code to •••• ••• 789…".</summary>
    public string SentText
    {
        get => _sentText;
        private set => SetProperty(ref _sentText, value);
    }

    public ICommand LoadCommand { get; }

    public ICommand VerifyCommand { get; }

    public ICommand DismissMessageCommand { get; }

    /// <summary>Call when the screen appears. A new code starts with an empty field; without one, back to the time screen.</summary>
    public async Task LoadAsync()
    {
        if (_session.VerificationId is not { } verificationId)
        {
            await _navigator.ShowTimeAsync().ConfigureAwait(true);
            return;
        }

        if (_loadedFor != verificationId)
        {
            _loadedFor = verificationId;
            Code = string.Empty;
            CodeError = null;
            Message = null;
        }

        SentText = Text.Format(TextKeys.CodeSent, _session.MaskedPhone ?? string.Empty);
    }

    public async Task VerifyAsync()
    {
        if (IsBusy)
        {
            return;
        }

        Message = null;
        if (!CanVerify)
        {
            CodeError = Text[TextKeys.CodeInvalid];
            return;
        }

        if (_session.VerificationId is not { } verificationId)
        {
            await _navigator.ShowTimeAsync().ConfigureAwait(true);
            return;
        }

        IsBusy = true;
        var result = await _api.VerifyAsync(verificationId, new VerifyRequest(_code)).ConfigureAwait(true);
        IsBusy = false;

        if (result.IsOffline)
        {
            Message = Text[TextKeys.Offline];
            return;
        }

        if (result.IsRefused)
        {
            await OnRefusedAsync(result.Problem, result.StatusCode).ConfigureAwait(true);
            return;
        }

        await OnAnswerAsync(result.Value!).ConfigureAwait(true);
    }

    private async Task OnAnswerAsync(VerifyResponse response)
    {
        switch (response.Outcome)
        {
            case VerifyOutcomes.Booked when response.AppointmentId is { } appointmentId:
                _session.ClearVerification();
                _session.AppointmentId = appointmentId;
                await _navigator.ShowDoneAsync().ConfigureAwait(true);
                break;
            case VerifyOutcomes.WrongCode when response.AttemptsLeft > 0:
                CodeError = response.AttemptsLeft == 1 ? Text[TextKeys.CodeWrongOne] : Text.Format(TextKeys.CodeWrongMany, response.AttemptsLeft);
                break;
            case VerifyOutcomes.WrongCode:
            case VerifyOutcomes.TooManyAttempts:
                await BackToTimeAsync(TextKeys.CodeTooManyAttempts, timeGone: false).ConfigureAwait(true);
                break;
            case VerifyOutcomes.Expired:
                await BackToTimeAsync(TextKeys.CodeExpired, timeGone: false).ConfigureAwait(true);
                break;
            case VerifyOutcomes.SlotTaken:
                await BackToTimeAsync(TextKeys.SlotGone, timeGone: true).ConfigureAwait(true);
                break;
            case VerifyOutcomes.NotFound:
                await BackToTimeAsync(TextKeys.CodeNotFound, timeGone: false).ConfigureAwait(true);
                break;
            default:
                Message = Text[TextKeys.Unexpected];
                break;
        }
    }

    private async Task OnRefusedAsync(ApiProblem problem, int status)
    {
        if (status == 409 || string.Equals(problem.Code, ApiProblemCodes.SlotTaken, StringComparison.Ordinal))
        {
            await BackToTimeAsync(TextKeys.SlotGone, timeGone: true).ConfigureAwait(true);
            return;
        }

        if (status == 404 || string.Equals(problem.Code, ApiProblemCodes.NotFound, StringComparison.Ordinal))
        {
            await BackToTimeAsync(TextKeys.CodeNotFound, timeGone: false).ConfigureAwait(true);
            return;
        }

        if (status == 400 || string.Equals(problem.Code, ApiProblemCodes.Validation, StringComparison.Ordinal))
        {
            CodeError = string.IsNullOrWhiteSpace(problem.Message) ? Text[TextKeys.CodeInvalid] : problem.Message;
            return;
        }

        Message = string.IsNullOrWhiteSpace(problem.Message) ? Text[TextKeys.Unexpected] : problem.Message;
    }

    /// <summary>The visit stays as chosen; the code is spent. A time taken meanwhile goes too.</summary>
    private async Task BackToTimeAsync(string messageKey, bool timeGone)
    {
        if (timeGone)
        {
            _session.TimeHasGone();
        }
        else
        {
            _session.ClearVerification();
        }

        _loadedFor = null;
        _session.PostNotice(Text[messageKey]);
        await _navigator.ShowTimeAsync().ConfigureAwait(true);
    }
}