using Salon.Mobile.Navigation;

namespace Salon.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        ShellNavigator.RegisterRoutes();
    }
}