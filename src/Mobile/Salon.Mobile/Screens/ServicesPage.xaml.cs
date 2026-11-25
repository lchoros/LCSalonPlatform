using System.ComponentModel;
using Salon.Mobile.Screens.Contracts;
using Salon.Mobile.Screens.Parts;

namespace Salon.Mobile.Screens;

public partial class ServicesPage : ContentPage
{
    public ServicesPage(IServicesScreen screen)
    {
        Screen = screen;
        BindingContext = screen;
        InitializeComponent();
    }

    /// <summary>The screen, for bindings inside item templates (RelativeSource to this page).</summary>
    public IServicesScreen Screen { get; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Screen.PropertyChanged += OnScreenChanged;
        await Screen.AppearingAsync();
    }

    protected override void OnDisappearing()
    {
        Screen.PropertyChanged -= OnScreenChanged;
        base.OnDisappearing();
    }

    /// <summary>Android's back closes the sheet first, then leaves "add" mode for the booking.</summary>
    protected override bool OnBackButtonPressed()
    {
        if (Screen.Reading is not null)
        {
            Screen.CloseReadingCommand.Execute(null);
            return true;
        }

        if (Screen.IsAdding)
        {
            Screen.BackCommand.Execute(null);
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private void OnScreenChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IServicesScreen.Reading))
        {
            Dispatcher.Dispatch(async () =>
            {
                if (Screen.Reading is { } row)
                {
                    await Sheet.ShowAsync(ReadingSheet, row);
                }
                else
                {
                    await Sheet.HideAsync(ReadingSheet);
                }
            });
        }
    }
}