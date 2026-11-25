namespace Salon.Mobile.Screens.Contracts;

/// <summary>The screens of the booking, in the order a client meets them.</summary>
public enum AppScreen
{
    /// <summary>The service list; going here starts over.</summary>
    Services,

    /// <summary>The service list in "add another service" mode, over the time screen.</summary>
    AddService,

    Time,
    Details,
    Code,

    /// <summary>The confirmation; going here leaves nothing behind it but the service list.</summary>
    Done,
}

/// <summary>How a screen model moves the client on, without knowing MAUI. Implemented over Shell by ShellNavigator.</summary>
public interface INavigator
{
    /// <summary>Back to <paramref name="screen"/> when it is already open underneath, forward to it otherwise.</summary>
    Task GoToAsync(AppScreen screen);

    /// <summary>One screen back.</summary>
    Task BackAsync();

    /// <summary>Asks "Discard booking?"; true when the client chose to leave.</summary>
    Task<bool> ConfirmDiscardAsync();

    /// <summary>Opens an address on the salon's host in the phone's browser (privacy notice, manage link).</summary>
    Task OpenBrowserAsync(Uri address);
}