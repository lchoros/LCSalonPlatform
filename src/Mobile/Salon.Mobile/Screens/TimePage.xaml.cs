using System.ComponentModel;
using Salon.Mobile.Screens.Contracts;
using Salon.Mobile.Screens.Parts;

namespace Salon.Mobile.Screens;

public partial class TimePage : ContentPage
{
    public TimePage(ITimeScreen screen)
    {
        Screen = screen;
        BindingContext = screen;
        InitializeComponent();
    }

    /// <summary>The screen, for bindings inside item templates (RelativeSource to this page).</summary>
    public ITimeScreen Screen { get; }

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

    /// <summary>Android's back closes the sheet first; otherwise it is the cross, which asks before leaving.</summary>
    protected override bool OnBackButtonPressed()
    {
        if (Screen.StaffSheet is { } sheet)
        {
            sheet.CloseCommand.Execute(null);
        }
        else
        {
            Screen.CloseCommand.Execute(null);
        }

        return true;
    }

    private void OnScreenChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ITimeScreen.StaffSheet):
                Dispatcher.Dispatch(async () =>
                {
                    if (Screen.StaffSheet is { } sheet)
                    {
                        await Sheet.ShowAsync(StaffSheetView, sheet);
                    }
                    else
                    {
                        await Sheet.HideAsync(StaffSheetView);
                    }
                });
                break;

            // Each answer is a new list, which starts at its beginning: bring the chosen day and time back into
            // sight (the web's booking-slots.js does the same after a tap).
            case nameof(ITimeScreen.Days):
                Dispatcher.Dispatch(async () => await BringChosenIntoSight(DayStrip, DayRow));
                break;
            case nameof(ITimeScreen.Times):
                Dispatcher.Dispatch(async () => await BringChosenIntoSight(TimeStrip, TimeRow));
                break;
        }
    }

    private static async Task BringChosenIntoSight(ScrollView strip, Layout row)
    {
        var chosen = row.Children
            .OfType<Element>()
            .FirstOrDefault(child => child.BindingContext is IDayTile { IsSelected: true } or ITimeChip { IsSelected: true });
        if (chosen is not null)
        {
            await strip.ScrollToAsync(chosen, ScrollToPosition.Center, animated: false);
        }
    }
}