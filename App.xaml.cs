using Lore.Services;
using Lore.ViewModels;
using Microsoft.UI.Xaml;

namespace Lore;

public partial class App : Application
{
    private MainWindow? _mainWindow;

    public App()
    {
        UseGregorianCalendar();
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        // Errors on background threads and in forgotten tasks never reach the handler
        // above; log those too.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Diagnostics.Log($"Unhandled (background): {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Diagnostics.Log($"Unobserved task error: {e.Exception}");
            e.SetObserved();
        };
    }

    // Dates are shown in the Windows regional format, but always in the Gregorian
    // calendar: a birth year must not appear as a Buddhist or Persian one. (Stored dates
    // are separately read and written with the invariant culture.)
    private static void UseGregorianCalendar()
    {
        var culture = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.CurrentCulture.Clone();
        if (culture.DateTimeFormat.Calendar is not System.Globalization.GregorianCalendar)
        {
            var gregorian = culture.OptionalCalendars.OfType<System.Globalization.GregorianCalendar>().FirstOrDefault();
            if (gregorian is not null) culture.DateTimeFormat.Calendar = gregorian;
            else culture = System.Globalization.CultureInfo.InvariantCulture;
        }
        System.Globalization.CultureInfo.CurrentCulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Splash goes up first and is painted before anything else is built; it then
        // stays over the main window until the one-time data load has finished.
        SplashWindow? splash = await SplashWindow.ShowAsync();

        // Everything below can fail before there is a main window to report it in: the
        // Swiss Ephemeris DLL missing, a corpus file damaged, the data folder out of
        // reach. Whatever happens the splash comes down, and a failure is shown in a
        // window of its own instead of leaving the splash up with nothing behind it.
        try
        {
            _mainWindow = BuildMainWindow();
            _mainWindow.Activate();
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Start-up failed: {ex}");
            splash?.CloseNow();
            ShowStartupFailure(ex);
            return;
        }

        if (splash is not null)
            await splash.CloseWhenAsync(_mainWindow.Initialized);
    }

    private static MainWindow BuildMainWindow()
    {
        string baseDir       = AppContext.BaseDirectory;
        string ephemerisPath = Path.Combine(baseDir, "Assets", "Ephemeris");
        string interpPath    = Path.Combine(baseDir, "Data", "interpretations.json");
        string dailyPath     = Path.Combine(baseDir, "Data", "daily.json");
        string synastryPath  = Path.Combine(baseDir, "Data", "synastry.json");

        var celebSvc    = new CelebrityService();
        var settings    = new SettingsService();
        var chartSvc    = new ChartService(ephemerisPath) { Settings = settings.Load() };
        var interpreter = new ChartInterpreter(interpPath);
        var userCharts  = new UserChartService();
        var cities      = new CityService();
        var hospitals   = new HospitalService();
        var transits    = new TransitService(chartSvc);
        var daily       = new DailyInterpreter(dailyPath);
        var synastry    = new SynastryInterpreter(synastryPath);
        var mainVm      = new MainViewModel(celebSvc, chartSvc, interpreter, userCharts, cities, hospitals,
                                            transits, daily, synastry, settings);

        var window = new MainWindow(mainVm);
        // What is still waiting to be written (the chart and view to come back to).
        window.Closed += (_, _) => settings.Flush();
        return window;
    }

    // Kept in a field so the window is not collected while it is on screen.
    private Window? _failureWindow;

    // Lore could not start. Says so, with what went wrong and where the details are;
    // closing the window ends the app.
    private void ShowStartupFailure(Exception ex)
    {
        try
        {
            string missing = ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
                ? "The Swiss Ephemeris library (sweph.dll) is missing or damaged. "
                : ex is FileNotFoundException or DirectoryNotFoundException or System.Text.Json.JsonException
                    ? "One of the files Lore is installed with is missing or damaged. "
                    : "";
            var text = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "Lore could not start.\n\n" + missing +
                       "Installing Lore again (or unzipping the portable copy afresh) usually puts this right; " +
                       "your own saved charts are kept elsewhere and are not affected.\n\n" +
                       $"What went wrong: {ex.Message}\n\n" +
                       $"The details are in {Diagnostics.LogFile}",
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Margin = new Thickness(28),
            };
            _failureWindow = new Window { Title = "Lore", Content = text };
            _failureWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(720, 420));
            _failureWindow.Closed += (_, _) => Exit();
            _failureWindow.Activate();
        }
        catch (Exception second)
        {
            Diagnostics.Log($"Start-up failure could not be shown: {second}");
            Exit();
        }
    }

    // An error in the interface is logged and the app carries on — but not silently:
    // the status bar says something went wrong and where the details are.
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Diagnostics.Log($"Unhandled: {e.Exception}");
        try
        {
            if (_mainWindow is not null)
                _mainWindow.ViewModel.StatusMessage =
                    $"Something went wrong ({e.Message}). Lore is still running; the details are in {Diagnostics.LogFile}.";
        }
        catch { /* reporting must never raise a second error */ }
    }
}
