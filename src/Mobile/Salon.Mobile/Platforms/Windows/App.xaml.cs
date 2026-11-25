namespace Salon.Mobile.WinUI;

/// <summary>The WinUI host: the Windows build exists to look at the screens on a desk.</summary>
public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}