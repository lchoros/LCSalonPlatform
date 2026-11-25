using Salon.Mobile.Screens.Contracts;

namespace Salon.Mobile.Screens;

public partial class CodePage : ContentPage
{
    public CodePage(ICodeScreen screen)
    {
        Screen = screen;
        BindingContext = screen;
        InitializeComponent();
    }

    public ICodeScreen Screen { get; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Screen.AppearingAsync();
        CodeEntry.Focus();
    }

    protected override bool OnBackButtonPressed()
    {
        Screen.BackCommand.Execute(null);
        return true;
    }
}