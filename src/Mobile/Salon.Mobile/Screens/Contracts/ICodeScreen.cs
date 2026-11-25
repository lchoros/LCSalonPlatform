using System.Windows.Input;

namespace Salon.Mobile.Screens.Contracts;

/// <summary>Step 4: the six digits texted to the client (BOOKING-API.md, "Code").</summary>
public interface ICodeScreen : IScreen
{
    /// <summary>Where the code went, most digits hidden (<c>StartBookingResponse.MaskedPhone</c>).</summary>
    string MaskedPhone { get; }

    string Code { get; set; }

    /// <summary>Set after a wrong code (<c>VerifyResponse.AttemptsLeft</c>); null before.</summary>
    int? AttemptsLeft { get; }

    ICommand VerifyCommand { get; }

    ICommand BackCommand { get; }
}