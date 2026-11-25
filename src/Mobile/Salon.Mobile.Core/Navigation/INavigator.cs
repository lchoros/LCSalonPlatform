namespace Salon.Mobile.Core.Navigation;

/// <summary>
/// How a screen model moves the app on. The MAUI app implements it; each method makes that screen the current one
/// (going back to it when it is already on the stack). Nothing travels in the call: the screens share a
/// <see cref="Booking.BookingSession"/>, and the screen shown calls its own <c>LoadAsync</c> when it appears.
/// </summary>
public interface INavigator
{
    Task ShowServicesAsync();

    Task ShowTimeAsync();

    Task ShowDetailsAsync();

    Task ShowCodeAsync();

    Task ShowDoneAsync();

    /// <summary>Opens an address in the phone's browser: the privacy notice, the visit's own manage page.</summary>
    Task OpenInBrowserAsync(Uri address);
}