using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Handlers;
using Salon.Mobile.Configuration;
using Salon.Mobile.Design;
using Salon.Mobile.Navigation;
using Salon.Mobile.Screens;
using Salon.Mobile.Screens.Contracts;

namespace Salon.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        var services = builder.Services;
        services.AddSingleton(SalonAppOptions.Load(typeof(MauiProgram).Assembly));
        services.AddSingleton<INavigator, ShellNavigator>();
        services.AddSingleton<AppShell>();
        services.AddTransient<ServicesPage>();
        services.AddTransient<TimePage>();
        services.AddTransient<DetailsPage>();
        services.AddTransient<CodePage>();
        services.AddTransient<DonePage>();
        AddScreens(services);

        PlainFields();
        return builder.Build();
    }

    /// <summary>
    /// The screens' state. Today the samples in Design/ (no network, made-up salon); the screen models of
    /// Salon.Mobile.Core take their place here and nowhere else. Singletons: one booking is made at a time, and the
    /// service list serves both its own page and the "add another service" page.
    /// </summary>
    private static void AddScreens(IServiceCollection services)
    {
        services.AddSingleton<SampleBooking>();
        services.AddSingleton<IServicesScreen, SampleServicesScreen>();
        services.AddSingleton<ITimeScreen, SampleTimeScreen>();
        services.AddSingleton<IDetailsScreen, SampleDetailsScreen>();
        services.AddSingleton<ICodeScreen, SampleCodeScreen>();
        services.AddSingleton<IDoneScreen, SampleDoneScreen>();
    }

    /// <summary>Entries sit inside a drawn field (Styles.xaml, "Field"), so the platform's own underline or box goes.</summary>
    private static void PlainFields()
    {
        EntryHandler.Mapper.AppendToMapping("PlainField", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif WINDOWS
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
#endif
        });
    }
}