using Salon.Mobile.Screens.Contracts;

namespace Salon.Mobile.Screens;

public partial class DetailsPage : ContentPage
{
    public DetailsPage(IDetailsScreen screen)
    {
        Screen = screen;
        BindingContext = screen;
        InitializeComponent();
    }

    public IDetailsScreen Screen { get; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Screen.AppearingAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        Screen.BackCommand.Execute(null);
        return true;
    }

    private void OnConsentTapped(object? sender, TappedEventArgs e) => Screen.MarketingConsent = !Screen.MarketingConsent;
}