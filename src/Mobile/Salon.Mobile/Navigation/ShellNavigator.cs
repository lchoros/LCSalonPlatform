using Salon.Mobile.Resources.Strings;
using Salon.Mobile.Screens;
using Salon.Mobile.Screens.Contracts;

namespace Salon.Mobile.Navigation;

/// <summary>
/// <see cref="INavigator"/> over Shell. The service list is the root; the other screens are pushed on it. Going to a
/// screen that is already open underneath goes back to it (the code screen's "pick another time" returns to the time
/// screen the client came from); the confirmation replaces everything but the root.
/// </summary>
internal sealed class ShellNavigator : INavigator
{
    public const string ServicesRoute = "services";
    public const string AddServiceRoute = "add";
    public const string TimeRoute = "time";
    public const string DetailsRoute = "details";
    public const string CodeRoute = "code";
    public const string DoneRoute = "done";

    public static void RegisterRoutes()
    {
        Routing.RegisterRoute(AddServiceRoute, typeof(ServicesPage));
        Routing.RegisterRoute(TimeRoute, typeof(TimePage));
        Routing.RegisterRoute(DetailsRoute, typeof(DetailsPage));
        Routing.RegisterRoute(CodeRoute, typeof(CodePage));
        Routing.RegisterRoute(DoneRoute, typeof(DonePage));
    }

    public Task GoToAsync(AppScreen screen) => OnMainThread(() =>
    {
        var shell = Shell.Current;
        switch (screen)
        {
            case AppScreen.Services:
                return shell.GoToAsync($"//{ServicesRoute}");
            case AppScreen.Done:
                return shell.GoToAsync($"//{ServicesRoute}/{DoneRoute}");
        }

        // Back to it when it is open underneath: count the pages above it and pop that many.
        var stack = shell.Navigation.NavigationStack;
        for (var i = stack.Count - 1; i >= 1; i--)
        {
            if (Is(stack[i], screen))
            {
                var above = stack.Count - 1 - i;
                return above == 0 ? Task.CompletedTask : shell.GoToAsync(string.Join('/', Enumerable.Repeat("..", above)));
            }
        }

        return shell.GoToAsync(Route(screen));
    });

    public Task BackAsync() => OnMainThread(() => Shell.Current.GoToAsync(".."));

    public Task<bool> ConfirmDiscardAsync() => OnMainThread(() =>
        Shell.Current.DisplayAlertAsync(AppResources.DiscardTitle, AppResources.DiscardText, AppResources.DiscardYes, AppResources.DiscardStay));

    public Task OpenBrowserAsync(Uri address) => OnMainThread(() =>
        Browser.Default.OpenAsync(address, BrowserLaunchMode.SystemPreferred));

    private static bool Is(Page page, AppScreen screen) => screen switch
    {
        AppScreen.AddService => page is ServicesPage,
        AppScreen.Time => page is TimePage,
        AppScreen.Details => page is DetailsPage,
        AppScreen.Code => page is CodePage,
        _ => false,
    };

    private static string Route(AppScreen screen) => screen switch
    {
        AppScreen.AddService => AddServiceRoute,
        AppScreen.Time => TimeRoute,
        AppScreen.Details => DetailsRoute,
        AppScreen.Code => CodeRoute,
        _ => throw new ArgumentOutOfRangeException(nameof(screen), screen, null),
    };

    private static Task OnMainThread(Func<Task> work) => MainThread.InvokeOnMainThreadAsync(work);

    private static Task<T> OnMainThread<T>(Func<Task<T>> work) => MainThread.InvokeOnMainThreadAsync(work);
}