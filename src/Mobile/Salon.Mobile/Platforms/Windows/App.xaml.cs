namespace Salon.Mobile.WinUI;

/// <summary>The WinUI host: the Windows build exists to look at the screens on a desk.</summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>
    /// Where a failure to start is written: a WinUI app that fails while starting ends with a bare exit code and no
    /// word of why, and this build is run from a terminal by somebody who needs the reason. Nothing personal goes in.
    /// </summary>
    public static string CrashFile => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "salon-mobile-crash.txt");

    public App()
    {
        UnhandledException += (_, e) => Record(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Record(e.ExceptionObject as Exception);
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            Record(ex);
            throw;
        }
    }

    protected override MauiApp CreateMauiApp()
    {
        try
        {
            return MauiProgram.CreateMauiApp();
        }
        catch (Exception ex)
        {
            Record(ex);
            throw;
        }
    }

    private static void Record(Exception? exception)
    {
        try
        {
            System.IO.File.WriteAllText(CrashFile, $"{DateTimeOffset.Now:u}{Environment.NewLine}{exception}");
        }
        catch (System.IO.IOException)
        {
            // Nowhere to write: the exit code is all there is.
        }
    }
}