namespace Salon.Mobile.Screens.Parts;

/// <summary>
/// A full-screen white sheet over the page (who with, a service's description), coming up gently (ADR 0094, stage 6).
/// The page shows and hides it when its screen says so; the sheet keeps its content while it goes, so it never
/// empties before it has faded.
/// </summary>
internal static class Sheet
{
    private const double Rise = 48;

    public static async Task ShowAsync(VisualElement sheet, object content)
    {
        sheet.BindingContext = content;
        if (sheet.IsVisible)
        {
            return;
        }

        sheet.TranslationY = Rise;
        sheet.Opacity = 0;
        sheet.IsVisible = true;
        await Task.WhenAll(sheet.TranslateToAsync(0, 0, 220, Easing.CubicOut), sheet.FadeToAsync(1, 180, Easing.CubicOut));
    }

    public static async Task HideAsync(VisualElement sheet)
    {
        if (!sheet.IsVisible)
        {
            return;
        }

        await Task.WhenAll(sheet.TranslateToAsync(0, Rise, 160, Easing.CubicIn), sheet.FadeToAsync(0, 160, Easing.CubicIn));
        sheet.IsVisible = false;
    }
}