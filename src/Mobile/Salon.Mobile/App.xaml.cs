namespace Salon.Mobile;

public partial class App : Application
{
    private readonly AppShell _shell;

    public App(AppShell shell)
    {
        InitializeComponent();

        // Light theme only for now: the colours in Resources/Styles are the light ones.
        UserAppTheme = AppTheme.Light;
        _shell = shell;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_shell);
#if WINDOWS
        // On a desk the window opens at a phone's size, so the screens are seen as they are meant to be.
        window.Width = 420;
        window.Height = 860;
#endif
        return window;
    }
}