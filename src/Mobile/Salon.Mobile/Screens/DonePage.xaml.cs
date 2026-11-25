using Salon.Mobile.Screens.Contracts;

namespace Salon.Mobile.Screens;

public partial class DonePage : ContentPage
{
    public DonePage(IDoneScreen screen)
    {
        Screen = screen;
        BindingContext = screen;
        InitializeComponent();
    }

    public IDoneScreen Screen { get; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Screen.AppearingAsync();
    }

    /// <summary>The booking is made: back means a new one, never the code screen again.</summary>
    protected override bool OnBackButtonPressed()
    {
        Screen.BookAnotherCommand.Execute(null);
        return true;
    }
}